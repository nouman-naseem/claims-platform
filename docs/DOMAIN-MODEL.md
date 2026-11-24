# Domain model

| Entity | Purpose | Key rules |
|---|---|---|
| **User** | Login identity, role (Claimant / ClaimsOfficer / Manager), market | Email unique |
| **Claimant** | Customer profile linked to a User | One per claimant user |
| **Policy** | Issued upstream; Motor or Property; coverage limit, validity | Linked to a claimant by holder email on registration |
| **Claim** | Aggregate root | Incident must be in the past and within policy period; amount > 0 |
| **ClaimDocument** | Uploaded evidence metadata (file in storage) | Not allowed once decided |
| **Assessment** | Officer's estimated loss and recommended payout (internal) | Only while UnderReview, by assigned officer |
| **Assignment** | History of who owned the claim | One open assignment at a time |
| **InformationRequest** | Officer question and claimant response | One open request at a time (state machine) |
| **Decision** | Final outcome, settlement amount, reason | Requires an assessment; approved <= claimed and <= policy limit |
| **ClaimEvent** | Immutable audit trail | Written by every state change |
| **OutboxMessage** | Integration event awaiting Kafka | Same transaction as the change |

## Kafka events (topic `claims.events`, key = claim id)

`ClaimSubmitted`, `ClaimAssigned`, `AdditionalInformationRequested`, `AdditionalInformationProvided`,
`ClaimApproved`, `ClaimRejected`. Envelope:

```json
{ "eventId": "guid", "eventType": "ClaimApproved", "claimId": "guid", "claimNumber": "CLM-SG-20261124-123456",
  "occurredAt": "2026-11-24T09:00:00Z", "data": { "approvedAmount": 1200.00, "currency": "SGD" } }
```

Document uploads and assessments are audited but intentionally **not** published (internal detail).

## Dashboard definitions

* **Outstanding**: not Approved/Rejected. **Pending decisions**: UnderReview.
* **Overdue**: Submitted/UnderReview and older than 14 days. The clock pauses while InformationRequested (waiting on the claimant).
* **Exposure**: sum of `EstimatedExposure` over outstanding claims (claimed amount, replaced by recommended payout after assessment, zero when decided), converted to USD with fixed indicative rates.
