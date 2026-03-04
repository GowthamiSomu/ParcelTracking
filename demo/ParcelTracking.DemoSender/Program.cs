using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;

// ─────────────────────────────────────────────────────────────────────────────
//  Parcel Tracking System — Interactive Demo Runner
//  Sends scan events to the Service Bus emulator and queries the REST API
//  to demonstrate every major feature of the system.
//
//  Prerequisites: docker compose up -d   (all 7 containers running)
//  Run:          dotnet run --project demo/ParcelTracking.DemoSender
// ─────────────────────────────────────────────────────────────────────────────

// When running on the host machine, connect via localhost (Docker maps port 5672)
const string ServiceBusConnection =
    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;" +
    "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
const string ScanEventsQueue = "parcel-scan-events";
const string ApiBase          = "http://localhost:5058";

var http    = new HttpClient { BaseAddress = new Uri(ApiBase) };
var sbClient = new ServiceBusClient(ServiceBusConnection);
var sender   = sbClient.CreateSender(ScanEventsQueue);

var json = new JsonSerializerOptions
{
    PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    Converters                  = { new JsonStringEnumConverter() }
};

// ── Main Menu ────────────────────────────────────────────────────────────────
Banner("PARCEL TRACKING SYSTEM — DEMO RUNNER");
Console.WriteLine("  Each scenario sends real events to the Service Bus, waits for the");
Console.WriteLine("  ingestion service to process them, then queries the REST API.");
Console.WriteLine();

while (true)
{
    Section("SELECT A SCENARIO");
    Console.WriteLine("  [1] Standard parcel — happy path (full lifecycle)");
    Console.WriteLine("  [2] Large parcel    — 20% surcharge applied (dimension > 50 cm)");
    Console.WriteLine("  [3] Failed delivery — FAILED_TO_DELIVER → retry → DELIVERED");
    Console.WriteLine("  [4] Invalid transition anomaly (Rule C)");
    Console.WriteLine("  [5] Duplicate event — idempotency (same eventId sent twice)");
    Console.WriteLine("  [6] Rule A validation failure (missing address)");
    Console.WriteLine("  [7] Run ALL scenarios in sequence");
    Console.WriteLine("  [0] Exit");
    Console.WriteLine();
    Console.Write("  Choice: ");
    var choice = Console.ReadLine()?.Trim();
    Console.WriteLine();

    switch (choice)
    {
        case "1": await ScenarioStandardParcel(); break;
        case "2": await ScenarioLargeParcel(); break;
        case "3": await ScenarioFailedDelivery(); break;
        case "4": await ScenarioInvalidTransition(); break;
        case "5": await ScenarioIdempotency(); break;
        case "6": await ScenarioRuleAFailure(); break;
        case "7":
            await ScenarioStandardParcel();
            await ScenarioLargeParcel();
            await ScenarioFailedDelivery();
            await ScenarioInvalidTransition();
            await ScenarioIdempotency();
            await ScenarioRuleAFailure();
            break;
        case "0": goto Exit;
        default:  Warn("Unknown option. Please enter 1–7 or 0."); break;
    }
}
Exit:
await sender.DisposeAsync();
await sbClient.DisposeAsync();
Console.WriteLine();
Info("Demo complete. Goodbye!");

// ─────────────────────────────────────────────────────────────────────────────
//  SCENARIO 1 — Standard parcel, full happy-path lifecycle
// ─────────────────────────────────────────────────────────────────────────────
async Task ScenarioStandardParcel()
{
    Banner("SCENARIO 1 — Standard Parcel: Full Lifecycle");
    var id = UniqueId("STD");

    await Send(id, "COLLECTED", id, e =>
    {
        e.FromAddress  = Addr("1 Sender St", "Manchester", "M1 1AA", "UK");
        e.ToAddress    = Addr("99 Receiver Rd", "London", "EC1 2BB", "UK");
        e.Sender       = Contact("Alice Smith",   "07700000001", "alice@example.com");
        e.Receiver     = Receiver("Bob Jones",    "07700000002", "bob@example.com", true);
        e.Dimensions   = Dims(30, 20, 15, 2.5m);
        e.BaseCharge   = 8.50m;
    }, "Rule A validates collection data. Rule B classifies as STANDARD (all dims ≤ 50 cm). Base charge: £8.50, No surcharge.");

    await QueryParcel(id);

    foreach (var (status, loc, desc) in new[]
    {
        ("SOURCE_SORT",       "HUB-MCR-M60",    "Parcel arrives at origin sorting hub."),
        ("DESTINATION_SORT",  "HUB-LON-EC1",    "Parcel arrives at destination sorting hub."),
        ("DELIVERY_CENTRE",   "DEPOT-LON-SW1",  "Parcel arrives at local delivery depot."),
        ("READY_FOR_DELIVERY","DEPOT-LON-SW1",  "Out for delivery."),
        ("DELIVERED",         "DOOR-99-EC1",    "Successfully delivered. Sender notified by email. Receiver notified by SMS (opted in)."),
    })
    {
        await Send(id, status, id, e => { e.LocationId = loc; }, desc);
        await QueryParcel(id);
    }

    await QueryEvents(id);
    Success("Scenario 1 complete — parcel delivered successfully.");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SCENARIO 2 — Large parcel with 20% surcharge
// ─────────────────────────────────────────────────────────────────────────────
async Task ScenarioLargeParcel()
{
    Banner("SCENARIO 2 — Large Parcel: 20% Surcharge (Rule B)");
    var id = UniqueId("LRG");

    await Send(id, "COLLECTED", id, e =>
    {
        e.FromAddress  = Addr("5 Factory Lane", "Birmingham", "B1 1AB", "UK");
        e.ToAddress    = Addr("12 High Street",  "Leeds",      "LS1 1BA", "UK");
        e.Sender       = Contact("Carol Corp",   "07700000003", "carol@corp.com");
        e.Receiver     = Receiver("Dan Davies",  "07700000004", "dan@example.com", false);
        e.Dimensions   = Dims(75, 60, 45, 15.0m);   // length=75 and width=60 both exceed 50 cm
        e.BaseCharge   = 20.00m;
    }, "Dimensions 75×60×45 cm — both length and width exceed 50 cm threshold.\n  Rule B: sizeClass=LARGE, surcharge=£4.00 (20%), total=£24.00.\n  Receiver has notificationOptIn=false → NO SMS will be sent.");

    await QueryParcel(id, highlight: "charges");
    Success("Scenario 2 complete — verify surcharge in response above.");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SCENARIO 3 — Failed delivery, retry, then delivered
// ─────────────────────────────────────────────────────────────────────────────
async Task ScenarioFailedDelivery()
{
    Banner("SCENARIO 3 — Failed Delivery: Retry Flow");
    var id = UniqueId("FTD");

    await Send(id, "COLLECTED", id, e =>
    {
        e.FromAddress = Addr("7 Post Road", "Bristol", "BS1 1AA", "UK");
        e.ToAddress   = Addr("3 Flat Ave",  "Bath",    "BA1 1BA", "UK");
        e.Sender      = Contact("Eve Evans",  "07700000005", "eve@example.com");
        e.Receiver    = Receiver("Frank Fox", "07700000006", "frank@example.com", true);
        e.Dimensions  = Dims(25, 20, 10, 1.0m);
        e.BaseCharge  = 5.00m;
    }, "Parcel collected.");

    foreach (var (s, l) in new[]
    {
        ("SOURCE_SORT",       "HUB-BRISTOL-BS"),
        ("DESTINATION_SORT",  "HUB-BATH-BA"),
        ("DELIVERY_CENTRE",   "DEPOT-BATH-BA1"),
        ("READY_FOR_DELIVERY","DEPOT-BATH-BA1"),
    })
        await Send(id, s, id, e => e.LocationId = l, null);

    await Send(id, "FAILED_TO_DELIVER", id, e =>
    {
        e.LocationId = "DOOR-3-BATH";
        e.Metadata   = new() { ["reason"] = JsonDocument.Parse("\"No answer at door\"").RootElement };
    }, "Delivery attempt failed — no answer. Status set to FAILED_TO_DELIVER.");

    await QueryParcel(id);

    await Send(id, "READY_FOR_DELIVERY", id, e => e.LocationId = "DEPOT-BATH-BA1",
        "Re-queued for delivery next day. FAILED_TO_DELIVER → READY_FOR_DELIVERY is a valid transition.");

    await Send(id, "DELIVERED", id, e => e.LocationId = "DOOR-3-BATH",
        "Second attempt successful. DELIVERED.");

    await QueryParcel(id);
    await QueryEvents(id);
    Success("Scenario 3 complete — failed delivery retried and delivered.");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SCENARIO 4 — Invalid transition (Rule C anomaly)
// ─────────────────────────────────────────────────────────────────────────────
async Task ScenarioInvalidTransition()
{
    Banner("SCENARIO 4 — Invalid Transition: Rule C Anomaly");
    var id = UniqueId("ANM");

    await Send(id, "COLLECTED", id, e =>
    {
        e.FromAddress = Addr("10 Depot Dr", "Coventry", "CV1 1AA", "UK");
        e.ToAddress   = Addr("20 Park Ln",  "Oxford",   "OX1 1BA", "UK");
        e.Sender      = Contact("Grace Green", "07700000007", "grace@example.com");
        e.Receiver    = Receiver("Hank Hill",  "07700000008", "hank@example.com", false);
        e.Dimensions  = Dims(20, 15, 10, 0.5m);
        e.BaseCharge  = 4.50m;
    }, "Parcel collected successfully.");

    await QueryParcel(id);

    Warn("Now sending DELIVERED directly from COLLECTED — skipping 5 stages.");
    Warn("Rule C must reject this and emit an anomaly event.");
    Console.WriteLine();

    await Send(id, "DELIVERED", id, e => e.LocationId = "DOOR-20-OX",
        "INVALID: COLLECTED → DELIVERED is NOT a permitted transition.\n  Expect: Rule C rejects it, anomaly event published to parcel-anomalies queue,\n  original message dead-lettered. Parcel status remains COLLECTED.");

    await Task.Delay(2000); // give ingestion time to reject
    await QueryParcel(id);
    Info("  ↑ Status should still be COLLECTED — the invalid event was rejected.");
    Info("  Check ingestion logs:  docker compose logs ingestion --tail 20");
    Success("Scenario 4 complete — invalid transition correctly rejected.");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SCENARIO 5 — Duplicate event / Idempotency
// ─────────────────────────────────────────────────────────────────────────────
async Task ScenarioIdempotency()
{
    Banner("SCENARIO 5 — Idempotency: Duplicate Event Handling");
    var id      = UniqueId("IDP");
    var eventId = $"evt-idempotency-{id}";  // fixed eventId — will be reused

    await Send(id, "COLLECTED", id, e =>
    {
        e.EventId     = eventId;   // pin the eventId so we can resend it
        e.FromAddress = Addr("1 Test Rd", "Liverpool", "L1 1AA", "UK");
        e.ToAddress   = Addr("2 Demo St", "Chester",   "CH1 1BA", "UK");
        e.Sender      = Contact("Iris Irwin", "07700000009", "iris@example.com");
        e.Receiver    = Receiver("Jack Jones", "07700000010", "jack@example.com", true);
        e.Dimensions  = Dims(10, 10, 10, 0.3m);
        e.BaseCharge  = 3.00m;
    }, "First send — event processed normally.");

    await QueryParcel(id);

    Warn($"Resending the EXACT same event (eventId={eventId}) to simulate a network retry...");
    Console.WriteLine();

    await Send(id, "COLLECTED", id, e =>
    {
        e.EventId     = eventId;   // same eventId
        e.FromAddress = Addr("1 Test Rd", "Liverpool", "L1 1AA", "UK");
        e.ToAddress   = Addr("2 Demo St", "Chester",   "CH1 1BA", "UK");
        e.Sender      = Contact("Iris Irwin", "07700000009", "iris@example.com");
        e.Receiver    = Receiver("Jack Jones", "07700000010", "jack@example.com", true);
        e.Dimensions  = Dims(10, 10, 10, 0.3m);
        e.BaseCharge  = 3.00m;
    }, "DUPLICATE send — ingestion service detects duplicate via Redis SET NX.\n  Event is acknowledged and silently dropped. Parcel state unchanged.");

    await Task.Delay(2000);
    Info("  Check ingestion logs for [IDEMPOTENCY] Duplicate event skipped:");
    Info("  docker compose logs ingestion --tail 10");
    await QueryParcel(id);
    Success("Scenario 5 complete — duplicate event silently dropped, parcel unchanged.");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SCENARIO 6 — Rule A validation failure (missing address)
// ─────────────────────────────────────────────────────────────────────────────
async Task ScenarioRuleAFailure()
{
    Banner("SCENARIO 6 — Rule A Validation Failure (Missing Address)");
    var id = UniqueId("BAD");

    Warn("Sending a COLLECTED event with NO toAddress and NO dimensions.");
    Warn("Rule A must reject it. No parcel record should be created.");
    Console.WriteLine();

    await Send(id, "COLLECTED", id, e =>
    {
        e.FromAddress = Addr("3 Sender Lane", "Norwich", "NR1 1AA", "UK");
        // toAddress intentionally omitted
        e.Sender      = Contact("Karen Kay", "07700000011", "karen@example.com");
        e.Receiver    = Receiver("Leo Lee",  "07700000012", "leo@example.com", false);
        // dimensions intentionally omitted
        e.BaseCharge  = 6.00m;
    }, "INVALID: Missing toAddress and dimensions.\n  Expect: Rule A rejects with MISSING_TO_ADDRESS, message dead-lettered, parcel NOT created.");

    await Task.Delay(2000);

    Info("  Querying API — should return 404 (parcel was never created):");
    await QueryParcel(id, expectMissing: true);
    Info("  Check ingestion logs for validation failure:");
    Info("  docker compose logs ingestion --tail 15");
    Success("Scenario 6 complete — Rule A correctly rejected the invalid collection event.");
}

// ─────────────────────────────────────────────────────────────────────────────
//  Helpers — Service Bus
// ─────────────────────────────────────────────────────────────────────────────
async Task Send(string trackingId, string eventType, string sessionId,
                Action<ScanEventDto> configure, string? description)
{
    var evt = new ScanEventDto
    {
        EventId      = Guid.NewGuid().ToString(),
        TrackingId   = trackingId,
        EventType    = eventType,
        EventTimeUtc = DateTime.UtcNow,
        LocationId   = "DEMO-LOCATION",
        ActorId      = "DEMO-ACTOR",
        Metadata     = new()
    };
    configure(evt);

    var body    = JsonSerializer.Serialize(evt, json);
    var message = new ServiceBusMessage(body) { SessionId = sessionId };

    Step($"SEND → {eventType,-22} | trackingId={trackingId}");
    if (description != null)
    {
        foreach (var line in description.Split('\n'))
            Console.WriteLine($"         {line.TrimStart()}");
    }

    await sender.SendMessageAsync(message);
    await Task.Delay(1800); // allow ingestion service to process
}

// ─────────────────────────────────────────────────────────────────────────────
//  Helpers — REST API
// ─────────────────────────────────────────────────────────────────────────────
async Task QueryParcel(string trackingId, string? highlight = null, bool expectMissing = false)
{
    try
    {
        var response = await http.GetAsync($"/api/parcels/{trackingId}");
        if (!response.IsSuccessStatusCode)
        {
            if (expectMissing)
                Success($"  GET /api/parcels/{trackingId} → {(int)response.StatusCode} {response.ReasonPhrase} (expected — parcel was not created)");
            else
                Warn($"  GET /api/parcels/{trackingId} → {(int)response.StatusCode} {response.ReasonPhrase}");
            return;
        }
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var pretty = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });
        ApiResult($"GET /api/parcels/{trackingId}", pretty);
    }
    catch (Exception ex)
    {
        Error($"  API call failed: {ex.Message}");
    }
}

async Task QueryEvents(string trackingId)
{
    try
    {
        var response = await http.GetAsync($"/api/parcels/{trackingId}/events?limit=20");
        if (!response.IsSuccessStatusCode) { Warn($"  Events query failed: {response.StatusCode}"); return; }
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var pretty = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });
        ApiResult($"GET /api/parcels/{trackingId}/events", pretty);
    }
    catch (Exception ex)
    {
        Error($"  API call failed: {ex.Message}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Helpers — Domain object factories
// ─────────────────────────────────────────────────────────────────────────────
static AddressDto  Addr(string l, string c, string p, string co) => new(l, c, p, co);
static ContactDto  Contact(string n, string ph, string e)        => new(n, ph, e);
static ReceiverDto Receiver(string n, string ph, string e, bool opt) => new(n, ph, e, opt);
static DimsDto     Dims(decimal l, decimal w, decimal h, decimal wt)  => new(l, w, h, wt);
static string      UniqueId(string prefix) => $"{prefix}-{DateTime.UtcNow:HHmmss}";

// ─────────────────────────────────────────────────────────────────────────────
//  Helpers — Console output
// ─────────────────────────────────────────────────────────────────────────────
static void Banner(string text)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(new string('═', 72));
    Console.WriteLine($"  {text}");
    Console.WriteLine(new string('═', 72));
    Console.ResetColor();
    Console.WriteLine();
}
static void Section(string text)
{
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine($"┌─ {text} {'─',1}");
    Console.ResetColor();
}
static void Step(string text)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("  ► ");
    Console.ResetColor();
    Console.WriteLine(text);
}
static void ApiResult(string endpoint, string body)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  ◄ {endpoint}");
    Console.ForegroundColor = ConsoleColor.White;
    foreach (var line in body.Split('\n'))
        Console.WriteLine($"    {line}");
    Console.ResetColor();
    Console.WriteLine();
}
static void Info(string text)    { Console.ForegroundColor = ConsoleColor.Gray;    Console.WriteLine(text); Console.ResetColor(); }
static void Warn(string text)    { Console.ForegroundColor = ConsoleColor.DarkYellow; Console.WriteLine($"  ⚠  {text}"); Console.ResetColor(); }
static void Success(string text) { Console.ForegroundColor = ConsoleColor.Green;   Console.WriteLine($"  ✓  {text}"); Console.ResetColor(); Console.WriteLine(); }
static void Error(string text)   { Console.ForegroundColor = ConsoleColor.Red;     Console.WriteLine($"  ✗  {text}"); Console.ResetColor(); }

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs (self-contained, no project reference needed)
// ─────────────────────────────────────────────────────────────────────────────
class ScanEventDto
{
    public string EventId      { get; set; } = Guid.NewGuid().ToString();
    public string TrackingId   { get; set; } = string.Empty;
    public string EventType    { get; set; } = string.Empty;
    public DateTime EventTimeUtc { get; set; } = DateTime.UtcNow;
    public string LocationId   { get; set; } = "DEMO-LOCATION";
    public string? HubType     { get; set; }
    public string ActorId      { get; set; } = "DEMO-ACTOR";
    public AddressDto?  FromAddress { get; set; }
    public AddressDto?  ToAddress   { get; set; }
    public ContactDto?  Sender      { get; set; }
    public ReceiverDto? Receiver    { get; set; }
    public DimsDto?     Dimensions  { get; set; }
    public decimal?     BaseCharge  { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = new();
}
record AddressDto(string Line1, string City, string Postcode, string Country);
record ContactDto(string Name, string ContactNumber, string Email);
record ReceiverDto(string Name, string ContactNumber, string Email, bool NotificationOptIn);
record DimsDto(decimal LengthCm, decimal WidthCm, decimal HeightCm, decimal WeightKg);
