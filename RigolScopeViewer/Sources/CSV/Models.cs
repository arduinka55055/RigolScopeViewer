namespace RigolScopeViewer.Sources.CSV;

public enum CsvImportMode
{
    Timestamped,   // 1 колонка = Час, далі Канали
    IntervalBased  // Часу немає, лише Канали. Інтервал задається вручну.
}

public class CsvSourceConfig
{
    public CsvImportMode Mode { get; set; } = CsvImportMode.Timestamped;
    public float ManualSampleInterval { get; set; } = 1e-6f; // 1 мікросекунда за замовчуванням
    public bool HasHeaderRow { get; set; } = true;
}
