namespace CarbonTrack.Models
{
    public enum RowStatus { Ready, NeedsReview, Gap }

    public class BulkRow
    {
        // Source reference
        public int SourceRowNumber { get; set; }

        // Raw parsed values
        public string? RawDate          { get; set; }
        public string? RawOrigin        { get; set; }
        public string? RawDestination   { get; set; }
        public string? RawMode          { get; set; }
        public string? RawDistance      { get; set; }
        public string? RawPassengers    { get; set; }
        public string? RawClass         { get; set; }

        // Resolved values
        public DateTime? TripDate       { get; set; }
        public string    Origin         { get; set; } = "";
        public string    Destination    { get; set; } = "";
        public string    TransportMode  { get; set; } = "";
        public string?   TravelClass    { get; set; }
        public double    DistanceKm     { get; set; }
        public bool      DistanceMiles  { get; set; }  // was input in miles, converted
        public int       Passengers     { get; set; } = 1;

        // Calculation results
        public double    EmissionFactor { get; set; }
        public double    WttFactor      { get; set; }
        public double    KgCO2e         { get; set; }
        public double    KgCO2eWtt      { get; set; }  // with WTT uplift
        public double    KgCO2eRfi      { get; set; }  // with RFI (flights only)
        public string    Formula        { get; set; } = "";
        public string    DefraYear      { get; set; } = "DEFRA 2025";
        public string    Methodology    { get; set; } = "";
        public string    ModeConfidence { get; set; } = "high"; // high / medium / low / none

        // Status
        public RowStatus Status         { get; set; } = RowStatus.Ready;
        public List<string> Issues      { get; set; } = new();

        // Audit metadata (stored as JSON in notes or separate table later)
        public string AuditJson => System.Text.Json.JsonSerializer.Serialize(new
        {
            source_row          = SourceRowNumber,
            raw_mode            = RawMode,
            raw_distance        = RawDistance,
            mode_confidence     = ModeConfidence,
            distance_miles_converted = DistanceMiles,
            defra_year          = DefraYear,
            ef_combustion       = EmissionFactor,
            ef_wtt              = WttFactor,
            calculation_utc     = DateTime.UtcNow.ToString("o"),
        });
    }

    public class ColumnMap
    {
        public int? DateCol        { get; set; }
        public int? OriginCol      { get; set; }
        public int? DestCol        { get; set; }
        public int? ModeCol        { get; set; }
        public int? DistanceCol    { get; set; }
        public int? PassengersCol  { get; set; }
        public int? ClassCol       { get; set; }
        public bool DistanceIsMiles { get; set; }
    }

    public class ParseResult
    {
        public bool          Success        { get; set; }
        public string        ErrorMessage   { get; set; } = "";
        public List<string>  DetectedHeaders { get; set; } = new();
        public ColumnMap     ColumnMap      { get; set; } = new();
        public List<BulkRow> Rows           { get; set; } = new();
        public int           ReadyCount     => Rows.Count(r => r.Status == RowStatus.Ready);
        public int           ReviewCount    => Rows.Count(r => r.Status == RowStatus.NeedsReview);
        public int           GapCount       => Rows.Count(r => r.Status == RowStatus.Gap);
        public double        TotalKgCO2e    => Math.Round(Rows.Where(r => r.Status != RowStatus.Gap).Sum(r => r.KgCO2e), 2);
    }
}
