using StatePipes.Messages;
using StatePipes.SelfDescription;

namespace StatePipes.OpcUaBridge
{
    /// <summary>
    /// Builds the <see cref="TypeSerializationList"/> the bridge answers <see cref="GetSelfDescriptionCommand"/>
    /// with. A compiled StatePipes service reflects over its message classes; the bridge has no classes for its
    /// Get commands and events, so it writes the same descriptions by hand. Clients such as StatePipes.Explorer
    /// cannot tell the difference and emit real types from them.
    ///
    /// <para>Every Get event carries the same envelope around the value: the node it came from and the OPC UA
    /// status and timestamps of the read. Value is nullable because a failed read has no value to give.</para>
    ///
    /// <para>Property types are described by name only. Every type an event uses is one of the base types
    /// clients resolve by name before looking at a description, so reflecting them in full -- as the converter
    /// does, pulling in DateTime's Year, Month, DayOfWeek and so on -- only multiplies the message size, which
    /// matters with thousands of nodes.</para>
    /// </summary>
    internal static class SelfDescriptionBuilder
    {
        public const string NodeIdProperty = "NodeId";
        public const string ValueProperty = "Value";
        public const string StatusCodeProperty = "StatusCode";
        public const string StatusProperty = "Status";
        public const string SourceTimestampProperty = "SourceTimestamp";
        public const string ServerTimestampProperty = "ServerTimestamp";

        /// <summary>The StatePipes messages the bridge itself handles or publishes, described from the real types.</summary>
        private static readonly Type[] StatePipesMessageTypes = [typeof(GetSelfDescriptionCommand), typeof(SelfDescriptionEvent), typeof(HeartbeatEvent)];

        public static TypeSerializationList Build(IEnumerable<OpcUaDataItem> items)
        {
            TypeSerializationConverter converter = new();
            TypeSerializationList list = new();
            foreach (var type in StatePipesMessageTypes) list.TypeSerializations.Add(converter.CreateFromType(type));
            foreach (var item in items)
            {
                list.TypeSerializations.Add(BuildCommand(item));
                list.TypeSerializations.Add(BuildEvent(item));
            }
            return list;
        }

        private static TypeSerialization BuildCommand(OpcUaDataItem item)
        {
            var description = CreateMessageDescription(item.Names.Namespace, item.Names.CommandTypeName);
            description.IsCommand = true;
            TypeSerialization serialization = new() { FullName = description.FullName };
            serialization.AddTypeDescription(description);
            return serialization;
        }

        private static TypeSerialization BuildEvent(OpcUaDataItem item)
        {
            var description = CreateMessageDescription(item.Names.Namespace, item.Names.EventTypeName);
            description.IsEvent = true;
            TypeSerialization serialization = new() { FullName = description.FullName };
            serialization.AddTypeDescription(description);
            AddProperty(serialization, description, NodeIdProperty, typeof(string));
            AddProperty(serialization, description, ValueProperty, item.ValueType, isNullable: item.ValueType.IsValueType);
            AddProperty(serialization, description, StatusCodeProperty, typeof(uint));
            AddProperty(serialization, description, StatusProperty, typeof(string));
            AddProperty(serialization, description, SourceTimestampProperty, typeof(DateTime));
            AddProperty(serialization, description, ServerTimestampProperty, typeof(DateTime));
            return serialization;
        }

        private static TypeDescription CreateMessageDescription(string @namespace, string name) => new()
        {
            FullName = $"{@namespace}.{name}",
            Namespace = @namespace,
            AssemblyName = OpcUaNaming.AssemblyName,
            QualifiedName = $"{@namespace}.{name}, {OpcUaNaming.AssemblyName}"
        };

        private static void AddProperty(TypeSerialization serialization, TypeDescription owner, string name, Type type, bool isNullable = false)
        {
            owner.Properties.Add(new ParameterDescription(name, type.FullName!, isNullable, []));
            AddTypeByName(serialization, type);
        }

        private static void AddTypeByName(TypeSerialization serialization, Type type)
        {
            if (serialization.HasTypeDescription(type.FullName!)) return;
            TypeDescription description = new();
            description.SetNames(type);
            serialization.AddTypeDescription(description);
            if (!type.IsArray) return;
            var elementType = type.GetElementType()!;
            description.ArrayFullName = elementType.FullName!;
            description.ArrayRank = type.GetArrayRank();
            AddTypeByName(serialization, elementType);
        }
    }
}
