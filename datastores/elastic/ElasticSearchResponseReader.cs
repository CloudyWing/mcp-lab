using System.Globalization;

namespace CloudyWing.McpLab.Elastic;

/// <summary>
/// Converts the body of an Elasticsearch <c>_search</c> response to tool response data.
/// </summary>
public static class ElasticSearchResponseReader {
    /// <summary>
    /// Reads a search response. A complete body is parsed as a whole, and a truncated body yields the hits that are
    /// fully contained in it together with the truncation fields.
    /// </summary>
    /// <param name="body">The response body, read with a size limit.</param>
    /// <param name="maxResponseBytes">The limit that was applied while reading the body.</param>
    /// <returns>Data with the shape <c>{ total, hits }</c>, followed by the truncation fields when <paramref name="body"/> is truncated.</returns>
    public static object Read(BoundedBody body, int maxResponseBytes) {
        if (!body.Truncated) {
            JsonNode? root = JsonNode.Parse(body.Bytes);
            string total = root?["hits"]?["total"]?["value"]?.ToString() ?? "0";
            JsonArray hits = root?["hits"]?["hits"]?.AsArray() ?? [];
            List<object> output = [];

            foreach (JsonNode? hit in hits) {
                output.Add(ToHit(hit));
            }

            return new {
                total,
                hits = output,
            };
        }

        (string partialTotal, IReadOnlyList<object> partialHits) = ReadPartial(body.Bytes);

        return ResponseTruncation.Append(
            new {
                total = partialTotal,
                hits = partialHits,
            },
            body,
            maxResponseBytes
        );
    }

    private static object ToHit(JsonNode? hit) =>
        new {
            _id = hit?["_id"]?.ToString(),
            _score = hit?["_score"]?.ToString(),
            _source = hit?["_source"],
        };

    private static (string Total, IReadOnlyList<object> Hits) ReadPartial(byte[] bytes) {
        string total = "0";
        List<object> hits = [];

        try {
            Utf8JsonReader reader = new(bytes, isFinalBlock: false, state: default);

            if (reader.Read()
                && reader.TokenType == JsonTokenType.StartObject
                && MoveToProperty(ref reader, "hits"u8)
                && reader.TokenType == JsonTokenType.StartObject
            ) {
                ReadHitsObject(ref reader, bytes, ref total, hits);
            }
        } catch (JsonException) {
            hits.Clear();
        } catch (InvalidOperationException) {
            hits.Clear();
        }

        return (total, hits);
    }

    // Leaves the reader on the first token of the named property's value of the object the reader is positioned on.
    private static bool MoveToProperty(ref Utf8JsonReader reader, ReadOnlySpan<byte> name) {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName) {
            bool isTarget = reader.ValueTextEquals(name);

            if (!reader.Read()) {
                return false;
            }

            if (isTarget) {
                return true;
            }

            if (!reader.TrySkip()) {
                return false;
            }
        }

        return false;
    }

    private static void ReadHitsObject(ref Utf8JsonReader reader, byte[] bytes, ref string total, List<object> hits) {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName) {
            bool isTotal = reader.ValueTextEquals("total"u8);
            bool isHits = reader.ValueTextEquals("hits"u8);

            if (!reader.Read()) {
                return;
            }

            if (isTotal) {
                if (!ReadTotal(ref reader, ref total)) {
                    return;
                }
            } else if (isHits && reader.TokenType == JsonTokenType.StartArray) {
                ReadHitElements(ref reader, bytes, hits);
                return;
            } else if (!reader.TrySkip()) {
                return;
            }
        }
    }

    // Returns false when the body ends before the total has been consumed completely.
    private static bool ReadTotal(ref Utf8JsonReader reader, ref string total) {
        if (reader.TokenType == JsonTokenType.Number) {
            if (reader.TryGetInt64(out long number)) {
                total = number.ToString(CultureInfo.InvariantCulture);
            }

            return true;
        }

        if (reader.TokenType != JsonTokenType.StartObject) {
            return reader.TrySkip();
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName) {
            bool isValue = reader.ValueTextEquals("value"u8);

            if (!reader.Read()) {
                return false;
            }

            if (isValue && reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long value)) {
                total = value.ToString(CultureInfo.InvariantCulture);
            } else if (!reader.TrySkip()) {
                return false;
            }
        }

        return reader.TokenType == JsonTokenType.EndObject;
    }

    private static void ReadHitElements(ref Utf8JsonReader reader, byte[] bytes, List<object> hits) {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject) {
            long start = reader.TokenStartIndex;

            if (!reader.TrySkip()) {
                return;
            }

            long end = reader.BytesConsumed;
            JsonNode? hit = JsonNode.Parse(bytes.AsSpan((int)start, (int)(end - start)));
            hits.Add(ToHit(hit));
        }
    }
}
