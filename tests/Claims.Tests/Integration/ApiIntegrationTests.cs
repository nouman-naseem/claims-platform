using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Claims.Application.Contracts;
using Claims.Domain.Enums;
using Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Claims.Tests.Integration;

public class ApiIntegrationTests : IClassFixture<ApiFactory>
{
    private const string Password = "Password123!";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly ApiFactory _factory;
    public ApiIntegrationTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email, string password = Password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), Json);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"Unexpected {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<ClaimDetailDto> SubmitClaimAsync(HttpClient claimant, decimal amount = 1_500m)
    {
        var policies = await claimant.GetFromJsonAsync<List<PolicyDto>>("/api/policies/mine", Json);
        var motor = policies!.First(p => p.Type == ClaimType.Motor);
        var response = await claimant.PostAsJsonAsync("/api/claims", new SubmitClaimRequest(
            motor.Id, DateTime.UtcNow.Date.AddDays(-2), "Orchard Road", "Rear-end collision at the traffic lights.", amount), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<ClaimDetailDto>(response);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_problem_details()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest("manager@claims.example", "wrong-password"), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var response = await _factory.CreateClient().GetAsync("/api/claims");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Role_based_authorization_is_enforced()
    {
        var claimant = await LoginAsync("claimant.sg1@example.com");
        var officer = await LoginAsync("officer.sg@claims.example");

        Assert.Equal(HttpStatusCode.Forbidden, (await claimant.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await claimant.GetAsync("/api/officers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.GetAsync("/api/policies/mine")).StatusCode);
    }

    [Fact]
    public async Task Invalid_claim_returns_400_with_field_errors()
    {
        var claimant = await LoginAsync("claimant.sg1@example.com");
        var response = await claimant.PostAsJsonAsync("/api/claims",
            new SubmitClaimRequest(Guid.NewGuid(), DateTime.UtcNow, "", "short", -5m), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ClaimedAmount", body);
        Assert.Contains("Description", body);
        Assert.Contains("IncidentLocation", body);
    }

    [Fact]
    public async Task Swagger_document_is_served()
    {
        var response = await _factory.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/claims", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Full_claim_lifecycle_across_all_three_roles()
    {
        var claimant = await LoginAsync("claimant.sg1@example.com");
        var officer = await LoginAsync("officer.sg@claims.example");
        var otherOfficer = await LoginAsync("officer.hk@claims.example");
        var otherClaimant = await LoginAsync("claimant.sg2@example.com");
        var manager = await LoginAsync("manager@claims.example");

        // 1. Claimant reports an incident
        var claim = await SubmitClaimAsync(claimant);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.StartsWith("CLM-SG-", claim.ClaimNumber);

        // 2. Other claimants cannot see it
        Assert.Equal(HttpStatusCode.NotFound, (await otherClaimant.GetAsync($"/api/claims/{claim.Id}")).StatusCode);

        // 3. Officer finds it in the unassigned queue and assigns it to themselves
        var queue = await ReadAsync<PagedResult<ClaimSummaryDto>>(
            await officer.GetAsync($"/api/claims?unassigned=true&search={claim.ClaimNumber}"));
        Assert.Equal(claim.Id, Assert.Single(queue.Items).Id);
        var assigned = await ReadAsync<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{claim.Id}/assign", new AssignClaimRequest(null), Json));
        Assert.Equal(ClaimStatus.UnderReview, assigned.Status);

        // 4. Another officer cannot act on it
        var forbidden = await otherOfficer.PostAsJsonAsync($"/api/claims/{claim.Id}/approve", new ApproveClaimRequest(100m, null), Json);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // 5. Officer cannot decide before assessing (business rule -> 422)
        var early = await officer.PostAsJsonAsync($"/api/claims/{claim.Id}/approve", new ApproveClaimRequest(100m, null), Json);
        Assert.Equal((HttpStatusCode)422, early.StatusCode);

        // 6. Information request / response round trip
        var requested = await ReadAsync<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{claim.Id}/information-requests",
            new RequestInformationRequest("Please provide the police report.", DateTime.UtcNow.AddDays(7)), Json));
        Assert.Equal(ClaimStatus.InformationRequested, requested.Status);
        var requestId = requested.InformationRequests.Single().Id;
        var answered = await ReadAsync<ClaimDetailDto>(await claimant.PostAsJsonAsync(
            $"/api/claims/{claim.Id}/information-requests/{requestId}/response", new ProvideInformationRequest("Police report no. 8841 attached."), Json));
        Assert.Equal(ClaimStatus.UnderReview, answered.Status);

        // 7. Documents: reject a disguised executable, accept a PDF, claimant can download it
        using (var bad = new MultipartFormDataContent { { new ByteArrayContent("MZ evil"u8.ToArray()), "file", "report.exe" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await claimant.PostAsync($"/api/claims/{claim.Id}/documents", bad)).StatusCode);

        var pdfBytes = "%PDF-1.4 police report"u8.ToArray();
        using var good = new MultipartFormDataContent { { new ByteArrayContent(pdfBytes), "file", "police-report.pdf" } };
        var doc = await ReadAsync<DocumentDto>(await claimant.PostAsync($"/api/claims/{claim.Id}/documents", good));
        Assert.Equal("police-report.pdf", doc.FileName);
        Assert.Equal(pdfBytes, await claimant.GetByteArrayAsync($"/api/claims/{claim.Id}/documents/{doc.Id}"));

        // 8. Assess and approve
        await ReadAsync<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{claim.Id}/assessments",
            new AssessClaimRequest(1_400m, 1_200m, "Internal: damage consistent with report."), Json));
        var approved = await ReadAsync<ClaimDetailDto>(await officer.PostAsJsonAsync($"/api/claims/{claim.Id}/approve",
            new ApproveClaimRequest(1_200m, "Approved in line with assessment."), Json));
        Assert.Equal(ClaimStatus.Approved, approved.Status);

        // 9. Claimant sees decision + settlement, but not internal assessment notes
        var final = await ReadAsync<ClaimDetailDto>(await claimant.GetAsync($"/api/claims/{claim.Id}"));
        Assert.Equal(ClaimStatus.Approved, final.Status);
        Assert.Equal(1_200m, final.Decision!.ApprovedAmount);
        Assert.Equal("SGD", final.Decision.Currency);
        Assert.Empty(final.Assessments);
        Assert.Contains(final.History, h => h.Type == ClaimEventType.ClaimApproved);

        // 10. A decided claim is final: reassigning is a business rule violation
        var officers = await ReadAsync<List<OfficerDto>>(await manager.GetAsync("/api/officers"));
        var reassign = await manager.PostAsJsonAsync($"/api/claims/{claim.Id}/assign", new AssignClaimRequest(officers[0].Id), Json);
        Assert.Equal((HttpStatusCode)422, reassign.StatusCode);

        // 11. Every integration event went through the outbox
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        var types = await db.OutboxMessages.Where(m => m.Key == claim.Id.ToString()).Select(m => m.Type).ToListAsync();
        Assert.Equal(new[] { "AdditionalInformationProvided", "AdditionalInformationRequested", "ClaimApproved", "ClaimAssigned", "ClaimSubmitted" },
            types.OrderBy(t => t, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Manager_dashboard_reports_seeded_multi_market_data()
    {
        var manager = await LoginAsync("manager@claims.example");
        var dashboard = await ReadAsync<DashboardDto>(await manager.GetAsync("/api/dashboard"));

        Assert.True(dashboard.TotalClaims >= 48);
        Assert.True(dashboard.OutstandingClaims > 0);
        Assert.True(dashboard.OverdueClaims > 0);
        Assert.True(dashboard.EstimatedExposureUsd > 0);
        Assert.Equal(6, dashboard.ByMarket.Count);
        Assert.Equal(2, dashboard.ByType.Count);
        Assert.Contains(dashboard.ByStatus, s => s.Key == "Approved");
        Assert.Equal(8, dashboard.TeamWorkload.Count);
        Assert.Equal(dashboard.TotalClaims, dashboard.ByStatus.Sum(s => s.Count));
    }

    [Fact]
    public async Task Claim_listing_supports_paging_and_filters()
    {
        var officer = await LoginAsync("officer.sg@claims.example");

        var page1 = await ReadAsync<PagedResult<ClaimSummaryDto>>(await officer.GetAsync("/api/claims?page=1&pageSize=5"));
        var page2 = await ReadAsync<PagedResult<ClaimSummaryDto>>(await officer.GetAsync("/api/claims?page=2&pageSize=5"));
        Assert.Equal(5, page1.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));
        Assert.True(page1.TotalPages >= 10);

        var japan = await ReadAsync<PagedResult<ClaimSummaryDto>>(await officer.GetAsync("/api/claims?market=Japan&pageSize=100"));
        Assert.NotEmpty(japan.Items);
        Assert.All(japan.Items, i => Assert.Equal(Market.Japan, i.Market));

        var overdue = await ReadAsync<PagedResult<ClaimSummaryDto>>(await officer.GetAsync("/api/claims?overdue=true&pageSize=100"));
        Assert.All(overdue.Items, i => Assert.True(i.IsOverdue));

        Assert.Equal(HttpStatusCode.BadRequest, (await officer.GetAsync("/api/claims?pageSize=1000")).StatusCode);
    }

    [Fact]
    public async Task Registration_links_pre_issued_policies_by_email()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterClaimantRequest("new.customer@example.com", "Sup3rSecret", "New Customer", "+65 9000 0000", Market.Singapore), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await ReadAsync<AuthResponse>(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var policies = await ReadAsync<List<PolicyDto>>(await client.GetAsync("/api/policies/mine"));
        Assert.Equal(2, policies.Count);

        var duplicate = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterClaimantRequest("new.customer@example.com", "Sup3rSecret", "Again", "+65 9000 0000", Market.Singapore), Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var weak = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterClaimantRequest("weak@example.com", "password", "Weak", "+65 9000 0001", Market.Singapore), Json);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
    }
}
