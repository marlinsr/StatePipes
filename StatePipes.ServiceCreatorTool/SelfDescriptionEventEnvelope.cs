using System;
using System.Collections.Generic;
using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    // Matches the shape of SelfDescriptionEvent JSON: { "TypeList": { ... } }
    internal sealed class SelfDescriptionEventEnvelope
    {
        public TypeSerializationList? TypeList { get; set; }
    }
}
