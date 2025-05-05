using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CoreSignature
{
    public interface IContextFeatureFlag
    {
        object userID { get; set; }
        int organizationId { get; set; }
        IDictionary<string, object> AdditionalProperties { get; set; }
    }
    public interface IFlagFeatureFlag
    {
        string name { get; set; }
        bool value { get; set; }
    }
    public interface IDataFeatureFlag
    {
        IFlagFeatureFlag? flag { get; set; }
        string last_fetched_time { get; set; }
    }
    public interface IConfigFeatureFlag
    {
        int? Ttl { get; set; }
        string CoreUrl { get; set; }
    }
    public delegate Task ICallBackFunc(bool statusFlag);
    public interface IFeatureFlag
    {
        Task<bool> GetStatusFlag(IContextFeatureFlag ctx, string flag, bool defaultValue = true);
        Task CbStatusFlag(ICallBackFunc cb, IContextFeatureFlag ctx, string flag, bool defaultValue = true);
    }

    public interface IResponseFeatureFlag
    {
        IDataFeatureFlag data { get; set; }
    }
    public class FeatureFlag : IFeatureFlag
    {
        private readonly IDatabase redis;
        private readonly int cacheTtlMs = 1000 * 60 * 30;
        private readonly string coreUrl;

        public FeatureFlag(IDatabase redis, IConfigFeatureFlag options = null)
        {
            this.redis = redis;
            this.cacheTtlMs = options?.Ttl ?? 1000 * 60 * 30;
            this.coreUrl = options?.CoreUrl ?? Environment.GetEnvironmentVariable("CORE_URL") ?? "http://localhost:7750/v1/feature-flag/dso";
        }

        private string GenerateCacheKey(string flag, IContextFeatureFlag ctx)
        {
            return $"feature-flag:{flag}:{ctx.userID}:{ctx.organizationId}";
        }

        private async Task<IDataFeatureFlag> GetFlag(IContextFeatureFlag ctx, string flag, bool defaultValue = true)
        {
            var cacheKey = GenerateCacheKey(flag, ctx);
            var cacheExist = await redis.StringGetAsync(cacheKey);

            if (cacheExist.HasValue)
            {
                return JsonConvert.DeserializeObject<IDataFeatureFlag>(cacheExist);
            }

            using var httpClient = new HttpClient();
            var content = new StringContent(JsonConvert.SerializeObject(new
            {
                context = ctx,
                flag_name = flag,
                default_value = defaultValue
            }), Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(coreUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                throw new Exception($"API error fetching flag \"{flag}\" for context {JsonConvert.SerializeObject(ctx)}: Status {response.StatusCode}, Body: {errorText}");
            }

            var json = JsonConvert.DeserializeObject<IResponseFeatureFlag>(await response.Content.ReadAsStringAsync());

            if (json == null || json.data == null || json.data.flag?.value == null)
            {
                throw new Exception($"Unexpected API response structure for flag \"{flag}\", context {JsonConvert.SerializeObject(ctx)}: {JsonConvert.SerializeObject(json)}");
            }

            var data = json.data;
            await redis.StringSetAsync(cacheKey, JsonConvert.SerializeObject(data), TimeSpan.FromMilliseconds(cacheTtlMs));

            return data;
        }

        public async Task<bool> GetStatusFlag(IContextFeatureFlag ctx, string flag, bool defaultValue = true)
        {
            var data = await GetFlag(ctx, flag, defaultValue);
            return data.flag.value;
        }

        public async Task CbStatusFlag(ICallBackFunc cb, IContextFeatureFlag ctx, string flag, bool defaultValue = true)
        {
            var status = await GetStatusFlag(ctx, flag, defaultValue);
            await cb(status);
        }
    }
}
