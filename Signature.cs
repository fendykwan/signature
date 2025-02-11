using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.WebUtilities;
using System.IO;
using System.Text;
using StackExchange.Redis;

namespace CoreSignatures
{
    public class Request
    {
        public IDictionary<string, string>? Headers { get; set; }
        public object? Query { get; set; }
        public object? Body { get; set; }
    }
    public class MissingSignature : UnauthorizedAccessException
    {
        public MissingSignature() : base() { }
    }
    public static class SignatureFunctions
    {
        public static string saltNumber = "10";
        public static async Task<string> EncryptSignature(string query, string body, string publicKey)
        {
            string combinedData = $"{publicKey}|{JsonSerializer.Serialize(query)}|{JsonSerializer.Serialize(body)}";
            int salt_round = !string.IsNullOrEmpty(saltNumber) ? int.Parse(saltNumber) : 10;
            string salt = BCrypt.Net.BCrypt.GenerateSalt(salt_round);
            return await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(combinedData, salt));
        }
        public static async Task<bool> CompareSignature(string data, string encrypted)
        {
            return await Task.Run(() => BCrypt.Net.BCrypt.Verify(data, encrypted));
        }
    }
    public class SignatureResponse
    {
        public string? App_name { get; set; }
        public string? Public_Key { get; set; }
        public string? Scope { get; set; }
    }
    public class ResponseSignature
    {
        public SignatureResponse? Data { get; set; }
    }
    public class SignatureService
    {
        private readonly ConnectionMultiplexer? RedisSignature;
        private readonly IDatabase? CacheSignature;

        public HttpClient httpClient = new HttpClient();
        public string urlCoreApiManagement = "http://localhost:7750";
        public string redisHost = "localhost";
        public string redisPassword = "root";
        public int redisPort = 6379;
        public int redisExpired = 60;

        public SignatureService()
        {
            string connectionString = $"{redisHost},password={redisPassword}";
            try
            {
                RedisSignature = ConnectionMultiplexer.Connect(connectionString);
                CacheSignature = RedisSignature.GetDatabase();
            }
            catch (RedisConnectionException ex)
            {
                Console.WriteLine($"Error connecting to Redis: {ex.Message}");
                this.CacheSignature = null;
            }
        }

        public bool UseSignature(Request req)
        {
            if (req.Headers?.TryGetValue("x-api-key", out string publicKey) == true)
            {
                if (!(publicKey is string)) throw new MissingSignature();
            } else throw new MissingSignature();
            return true;
        }
        public async Task VerifySignature(Request req)
        {
            if (
                req.Headers?.TryGetValue("x-api-key", out string publicKey) != true ||
                req.Headers?.TryGetValue("x-api-signature", out string encryption) != true
            ) throw new MissingSignature();
            string query = JsonSerializer.Serialize(req.Query ?? new object());
            string body = await this.ReadJsonBodyAsync(req.Body as FileBufferingReadStream);
            await this.VerifySignatureKey(query, body, publicKey, encryption);
        }
        public async Task VerifySignatureKey(string query, string body, string publicKey, string encryption)
        {
            string cacheKey = $"signature:{publicKey}";
            string? cached = this.CacheSignature?.StringGet(cacheKey);
            if (string.IsNullOrEmpty(cached))
            {
                HttpResponseMessage response;
                try
                {
                    string url = $"{this.urlCoreApiManagement}/signature/verify/{publicKey}";
                    response = await this.httpClient.GetAsync(url);
                    string content = await response.Content.ReadAsStringAsync();
                    ResponseSignature? responseSignature = JsonSerializer.Deserialize<ResponseSignature>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new MissingSignature();
                    SignatureResponse data = responseSignature.Data ?? new SignatureResponse();
                    cached = $"{data.App_name}|{data.Public_Key}|{data.Scope}";
                    this.CacheSignature?.StringSet(cacheKey, cached, TimeSpan.FromSeconds(this.redisExpired));
                }
                catch (Exception err)
                {
                    Console.Error.WriteLine(err);
                    throw new MissingSignature();
                }
                if (response.StatusCode != HttpStatusCode.OK) throw new MissingSignature();
            }
            await this.CompareSignature(query, body, publicKey, encryption);
        }
        public async Task CompareSignature(string query, string body, string publicKey, string encryptionKey)
        {
            string data = $"{publicKey}|{JsonSerializer.Serialize(query)}|{JsonSerializer.Serialize(body)}";
            bool result = await SignatureFunctions.CompareSignature(data, encryptionKey);
            if (!result) throw new MissingSignature();
        }

        public async Task<string> ReadJsonBodyAsync(FileBufferingReadStream? stream)
        {
            if (stream == null || !stream.CanRead)
                return "";

            // Reset stream position to the beginning
            stream.Position = 0;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            string json = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(json))
                return "";

            // Deserialize JSON
            return json;
        }
    }

}