using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace UnityXRefMap.Yaml
{
    internal partial class XRefMapReference
    {
        [YamlMember(Alias = "uid")] public string Uid;
        [YamlMember(Alias = "name")] public string Name;
        [YamlMember(Alias = "fullName")] public string FullName;
        [YamlMember(Alias = "href")] public string Href;
        [YamlMember(Alias = "commentId")] public string CommentId;
        [YamlMember(Alias = "nameWithType")] public string NameWithType;
        [YamlMember(Alias = "type")] public string Type;

        public void FixHref(string apiUrl)
        {
            string href;

            // Namespaces point to documentation index
            if (CommentId.StartsWith("N:"))
            {
                href = "index";
            }
            else
            {
                // Trim UnityEngine and UnityEditor namespaces from href
                href = TrimNameSpaceRegex().Replace(Uid, "");

                // Fix href of constructors
                href = href.Replace(".#ctor", "-ctor");

                // Fix href of generics
                href = GenericRegex().Replace(href, "");
                href = href.Replace("`", "_");

                // Fix href of methods
                href = Method1Regex().Replace(href, "");
                href = Method2Regex().Replace(href, "");

                // Fix href of properties and fields
                href = PropertyRegex().Replace(href, "-$1");
            }

            Href = $"{apiUrl}{href}.html";
        }

        [GeneratedRegex(@"^(UnityEditor|UnityEngine)\.")]
        private static partial Regex TrimNameSpaceRegex();
        [GeneratedRegex(@"`{2}\d+")]
        private static partial Regex GenericRegex();
        [GeneratedRegex(@"\*$")]
        private static partial Regex Method1Regex();
        [GeneratedRegex(@"\(.*\)")]
        private static partial Regex Method2Regex();
        [GeneratedRegex(@"\.([a-z][^.]*)$")]
        private static partial Regex PropertyRegex();
    }
}
