using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Portarr.Core.Catalog;

/// <summary>Reads <c>github: { … }</c> (one account) and <c>github: [{ … }, { … }]</c> (several) into one document type.</summary>
internal sealed class GitHubSourcesConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(GitHubSourcesDocument);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var document = new GitHubSourcesDocument();
        if (parser.TryConsume<SequenceStart>(out _))
        {
            document.IsList = true;
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                document.Accounts.Add((GitHubDocument?)rootDeserializer(typeof(GitHubDocument)) ?? new GitHubDocument());
            }

            return document;
        }

        // A mapping: the strict deserializer still rejects unknown keys inside it.
        document.Accounts.Add((GitHubDocument?)rootDeserializer(typeof(GitHubDocument)) ?? new GitHubDocument());
        return document;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotSupportedException("The catalog is never written by Portarr.");
    }
}
