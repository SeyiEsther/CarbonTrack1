namespace CarbonTrack.Models
{
    public enum RowStatus { Ready, NeedsReview, Gap }

    public class BulkRow
    {
        public int    SourceRowNumber { get; set; }

        // Raw values as read from file
        public string? RawDateOut     { get; set; }
        public string? RawDateBack    { get; set; }
        public string? RawJourney     { get; set; }
        public string? RawOrigin      { get; set; }
        public string? RawDestination { get; set; }
        public string? RawMode        { get; set; }
        public string? RawDistance    { get; set; }
        public string? RawPassengers  { get; set; }
        public string? RawClass       { get; set; }

        // Resolved trip values
        public DateTime? TripDate      { get; set; }
        public string    Origin        { get; set; } = "";
        public string    Destination   { get; set; } = "";  // via-city for return trips
        public string    Via           { get; set; } = "";
        public bool      IsReturnTrip  { get; set; }
        public string    TransportMode { get; set; } = "";
        public string?   TravelClass   { get; set; }
        public int       Passengers    { get; set; } = 1;
        public double    DistanceMiles { get; set; }
        public double    DistanceKm    { get; set; }    // miles / 0.621
        public bool      InputWasMiles { get; set; }

        // Emission factor (WTW)
        public double    EmissionFactor { get; set; }
        public bool      IsVehicleKm    { get; set; }   // true = Car (no pax multiplier)

        // Calculated emissions
        public double    KgCO2e  { get; set; }
        public double    KgCO2   { get; set; }
        public double    KgCH4   { get; set; }
        public double    KgN2O   { get; set; }

        public string    Formula      { get; set; } = "";
        public string    DefraYear    { get; set; } = "";
        public string    Methodology  { get; set; } = "";
        public string    ModeConfidence { get; set; } = "high";

        // Status
        public RowStatus     Status { get; set; } = RowStatus.Ready;
        public List<string>  Issues { get; set; } = new();
    }

    public class ColumnMap
    {
        public int?  DateOutCol    { get; set; }
        public int?  DateBackCol   { get; set; }
        public int?  JourneyCol    { get; set; }   // single "Journey" column
        public int?  OriginCol     { get; set; }   // separate From/Origin column
        public int?  DestCol       { get; set; }   // separate To/Destination column
        public int?  ModeCol       { get; set; }
        public int?  DistanceCol   { get; set; }
        public int?  PassengersCol { get; set; }
        public int?  ClassCol      { get; set; }
        public bool  InputIsMiles  { get; set; }
    }

    public class ParseResult
    {
        public bool          Success         { get; set; }
        public string        ErrorMessage    { get; set; } = "";
        public List<string>  DetectedHeaders { get; set; } = new();
        public ColumnMap     ColumnMap       { get; set; } = new();
        public List<BulkRow> Rows            { get; set; } = new();
        public int  ReadyCount  => Rows.Count(r => r.Status == RowStatus.Ready);
        public int  ReviewCount => Rows.Count(r => r.Status == RowStatus.NeedsReview);
        public int  GapCount    => Rows.Count(r => r.Status == RowStatus.Gap);
        public double TotalKgCO2e => Math.Round(Rows.Where(r => r.Status != RowStatus.Gap).Sum(r => r.KgCO2e), 2);
    }
}
