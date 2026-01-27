using BlazorBootstrap;
using StatePipes.Common;
using StatePipes.ExplorerTypes;
using System.Text;


namespace StatePipes.Explorer.Components.Pages
{
    public partial class ChartViewer
    {
        private string PreviousEditorObjectString = string.Empty;
        private LineChart _lineChart = new();
        private readonly ChartData _data = new()
        {
            Labels = ["0"],
            Datasets =
        [
            new LineChartDataset()
            {
                Data =
                [
                    0.00
                ]
            }
        ]
        };

        private static LineChartOptions GetDefaultLineChartOption()
        {
            var options = new LineChartOptions();
            options.Interaction.Mode = InteractionMode.Index;
            options.Plugins.Title!.Text = "UNKOWN";
            options.Plugins.Title.Display = true;
            options.Plugins.Title.Font = new ChartFont { Size = 10 };
            options.Responsive = true;
            options.Scales.X!.Title = new ChartAxesTitle { Text = "UNKOWN", Display = true };
            options.Scales.Y!.Title = new ChartAxesTitle { Text = "UNKOWN", Display = true };
            options.MaintainAspectRatio = true;
            options.Plugins.Legend = new ChartPluginsLegend() { Display = true, Position = "bottom" };
            
            return options;
        }

        private LineChartOptions _options = GetDefaultLineChartOption();
        private bool _optionsChange = false;

        protected override void OnParametersSet()
        {
            var editorObjectString = string.Empty;
            if (EditorObject?.Value != null)
            {
                var bvLineChart = EditorObject.Value as SPELineChart;
                if (bvLineChart != null)
                {
                    editorObjectString = JsonUtility.GetJsonStringForObject(EditorObject.Value);
                }
            }

            if (PreviousEditorObjectString != editorObjectString)
            {
                UpdateChartDataAndOptions();
                UpdateChart();
            }
            PreviousEditorObjectString = editorObjectString;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await _lineChart.InitializeAsync(_data, _options);
            }
            await base.OnAfterRenderAsync(firstRender);
        }

        private void UpdateChart()
        {
            if (_optionsChange)
            {
                (_lineChart.UpdateAsync(_data, _options)).Wait();
            }
            else
            {
                (_lineChart.UpdateValuesAsync(_data)).Wait();
            }
            _optionsChange = false;
        }

        private void UpdateChartDataAndOptions()
        {
            if (EditorObject != null && EditorObject.Value != null)
            {
                var bvLineChart = EditorObject.Value as SPELineChart;
                if (bvLineChart == null) return;
                var labels = bvLineChart.DataPoints.OrderBy(d => d.X).Select(x => x.X.ToString()).ToList();
                if (labels == null) return;
                var charData = bvLineChart.DataPoints.OrderBy(d => d.X).Select(x => x.Y).ToList();
                if (charData == null) return;
                List<double?>? charDataList = [];
                charData.ForEach(d => charDataList.Add(d));

                _data.Labels = labels;
                _data.Datasets?.Clear();
                _data.Datasets?.Add(new LineChartDataset()
                    {
                        Label = "Data",
                        Data = charDataList,
                        BackgroundColor = "rgb(88, 80, 141)",
                        BorderColor = "rgb(88, 80, 141)",
                        BorderWidth = 2,
                        HoverBorderWidth = 4,
                    }
                );

                if(_options.Plugins.Title!.Text != bvLineChart.ChartTitle ||
                _options.Scales.X!.Title!.Text != bvLineChart.XAxisTitle ||
                _options.Scales.Y!.Title!.Text != bvLineChart.YAxisTitle)
                {
                    _options.Plugins.Title!.Text = bvLineChart.ChartTitle;
                    _options.Scales.X!.Title!.Text = bvLineChart.XAxisTitle;
                    _options.Scales.Y!.Title!.Text = bvLineChart.YAxisTitle;
                    _optionsChange = true;
                }
            }
        }

        public override bool GetJson(StringBuilder jsonStringBuilder, bool getName = true)
        {
            if (getName && EditorObject?.Name != null)
            {
                jsonStringBuilder.Append($"\"{EditorObject.Name}\": ");
            }
            var obj = EditorObject?.Value as SPELineChart;
            if (obj == null)
            {
                jsonStringBuilder.Append("{null}");
            }
            else
            {
                var editorObjectString = JsonUtility.GetJsonStringForObject(obj);
                jsonStringBuilder.Append($"\"{editorObjectString}\"");
            }
            return true;
        }
    }
}
