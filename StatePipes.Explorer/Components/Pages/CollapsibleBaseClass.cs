using Microsoft.AspNetCore.Components;

namespace StatePipes.Explorer.Components.Pages
{
    public class CollapsibleBaseClass : ComponentBase, IDisposable
    {
        [Parameter]
        public bool IsExpandedInitialState { get; set; }
        [CascadingParameter(Name = "ParentComponent")]
        public CollapsibleBaseClass? ParentComponent { get; set; }
        public bool IsExpanded { get; set; }
        protected bool IsCascadingExpand { get; set; }
        protected List<CollapsibleBaseClass> Divs = [];
        protected bool _firstTime = true;
        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (_firstTime)
            {
                IsExpanded = ParentComponent == null ?  IsExpandedInitialState : ParentComponent.IsCascadingExpand ? ParentComponent.IsExpanded : false;
                IsCascadingExpand = ParentComponent == null ? false : ParentComponent.IsCascadingExpand;
            }
            _firstTime = false;
        }
        public void SetChildrenCollapse(bool collapse, bool isCascading)
        {
            IsExpanded = !collapse;
            IsCascadingExpand = isCascading;
            foreach (var div in Divs)
            {
                div.SetChildrenCollapse(collapse, isCascading);
            }
        }
        protected override void OnInitialized()
        {
            ParentComponent?.AddChild(this);
            base.OnInitialized();
        }
        public void AddChild(CollapsibleBaseClass div)
        {
            Divs.Add(div);
            StateHasChanged();
        }
        public void RemoveChild(CollapsibleBaseClass div)
        {
            Divs.Remove(div);
            StateHasChanged();
        }
        void IDisposable.Dispose()
        {
            GC.SuppressFinalize(this);
            ParentComponent?.RemoveChild(this);
        }
    }
}
