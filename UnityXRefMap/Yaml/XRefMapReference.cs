using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace UnityXRefMap.Yaml
{
    internal class XRefMapReference
    {
        private static readonly string[] HrefNamespacesToTrim = { "UnityEditor", "UnityEngine" };
        
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
                href = Uid;

                // Trim UnityEngine and UnityEditor namespaces from href
                foreach (string hrefNamespaceToTrim in HrefNamespacesToTrim)
                {
                    href = href.Replace($"{hrefNamespaceToTrim}.", "");
                }

                // Fix href of constructors
                href = href.Replace(".#ctor", "-ctor");

                // Fix href of generics
                href = Regex.Replace(href, @"`{2}\d+", "");
                href = href.Replace("`", "_");

                // Fix href of methods
                href = Regex.Replace(href, @"\*$", "");
                href = Regex.Replace(href, @"\(.*\)", "");

                // Fix href of properties
                if (CommentId.StartsWith("P:") || CommentId.StartsWith("F:") || CommentId.StartsWith("M:"))
                {
                    href = Regex.Replace(href, @"\.([a-z].*)$", "-$1");
                }
            }

            Href = $"{apiUrl}{href}.html";
        }
    }
}
