using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ProbablyStolen_FlatStorage
{
    internal sealed class BulkFeatureContractResolver : DefaultContractResolver
    {
        protected override List<MemberInfo> GetSerializableMembers(Type objectType) => objectType == typeof(ItemFeature)
            ? typeof(ItemFeature).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Cast<MemberInfo>().ToList()
            : base.GetSerializableMembers(objectType);

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization) =>
            base.CreateProperties(type, type == typeof(ItemFeature) ? MemberSerialization.Fields : memberSerialization);
    }
}
