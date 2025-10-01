namespace StatePipes.ExplorerTypes
{
    public class SPELineChart(string chartTitle, string xAxisTitle, string yAxisTitle, IReadOnlyList<SPELineChartDataPoint> dataPoints)
    {
        public string ChartTitle { get; } = chartTitle;
        public string XAxisTitle { get; } = xAxisTitle;
        public string YAxisTitle { get; } = yAxisTitle;
        public IReadOnlyList<SPELineChartDataPoint> DataPoints { get; } = dataPoints;

    }
}
