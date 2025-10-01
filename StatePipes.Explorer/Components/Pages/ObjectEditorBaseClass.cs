using Microsoft.AspNetCore.Components;
using System.Text;

namespace StatePipes.Explorer.Components.Pages
{
    public class ObjectEditorBaseClass : ComponentBase, IDisposable
    {
        [CascadingParameter(Name = "ParentComponent")]
        public ObjectEditorBaseClass? ParentComponent { get; set; }
        [Parameter]
        public PropertyValueClass? EditorObject { get; set; }
        [Parameter]
        public bool IsReadOnly { get; set; }
        protected List<ObjectEditorBaseClass> Editors = [];
        protected override void OnInitialized()
        {
            ParentComponent?.AddChild(this);
            base.OnInitialized();
        }
        public void AddChild(ObjectEditorBaseClass editor)
        {
            Editors.Add(editor);
            StateHasChanged();
        }
        public void RemoveChild(ObjectEditorBaseClass editor)
        {
            Editors.Remove(editor);
            StateHasChanged();
        }
        void IDisposable.Dispose() => ParentComponent?.RemoveChild(this);
        public virtual bool GetJson(StringBuilder jsonStringBuilder, bool getName = true) { return false; }
    }
}
