
export async function renderDot(model, elementId) {
    var element = document.getElementById(elementId);
    const graphContainer = d3.select(element);
    const width = graphContainer.node().clientWidth;
    const height = graphContainer.node().clientHeight;

    graphContainer.graphviz()
        .width(width)
        .height(height)
        .fit(true)
        .renderDot(model);
}