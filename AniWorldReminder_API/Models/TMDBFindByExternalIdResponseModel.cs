using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class TMDBFindByExternalIdResponseModel
    {
        [JsonPropertyName("tv_results")]
        public List<Result> TvResults { get; set; } = [];
    }
}
