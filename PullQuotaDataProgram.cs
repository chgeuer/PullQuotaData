namespace ConsoleApp1
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web;

    class Program
    {
        static void Main(string[] args) => MainAsync(args).Wait();

        static async Task MainAsync(string[] args)
        {
            Console.Write("Please enter your Azure Tenant ID (the GUID, not the .onmicrosoft.com thing): ");
            var tenantId = Console.ReadLine().Trim();

            // "942023a6-efbe-4d97-a72d-532ef7337595"

            var l = new List<Quota>();

            var token = TokenAsync(tenantId: tenantId);
            foreach (var subscriptionId in await SubscriptionIDs(token))
            {
                foreach (var location in await Locations(token, subscriptionId))
                {
                    l.AddRange(await ComputeQuota(token, subscriptionId, location));
                    l.AddRange(await NetworkQuota(token, subscriptionId, location));
                }
            }

            File.WriteAllLines("quota.tsv", l.Select(_ => _.ToString()).ToArray());
        }

        static string TokenAsync(string tenantId)
        {
            var fname = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure", "accessTokens.json");
            Console.WriteLine($"Reading {fname}");
            var f = File.ReadAllText(fname);

            var token =
                JsonConvert.DeserializeObject<Token[]>(f)
                .Where(_ => _._authority == $"https://login.microsoftonline.com/{tenantId}")
                .FirstOrDefault();

            return token.accessToken;
        }

        private static string ToQueryString(NameValueCollection nvc)
        {
            var array = (from key in nvc.AllKeys
                         from value in nvc.GetValues(key)
                         select string.Format("{0}={1}",
                         HttpUtility.UrlEncode(key),
                         HttpUtility.UrlEncode(value)))
                .ToArray();
            return "?" + string.Join("&", array);
        }

        static async Task<string> Request(string token, string uri, string api_version)
        {
            var baseUri = $"https://management.azure.com{uri}";
            var query = new NameValueCollection { { "api-version", api_version } };
            var msg = new HttpRequestMessage(
                method: HttpMethod.Get,
                requestUri: $"{baseUri}{query.ToQueryString()}");
            msg.Headers.Add("Authorization", $"Bearer {token}");

            var client = new HttpClient();
            var response = await client.SendAsync(msg);
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                return null;
            }
            var body = await response.Content.ReadAsStringAsync();

            return body;
        }

        static async Task<string[]> SubscriptionIDs(string token)
        {
            var body = await Request(token, "/subscriptions", "2016-06-01");
            var x = JObject.Parse(body);
            return x["value"].Select(_ => (string)_["subscriptionId"]).ToArray();
        }

        static async Task<string[]> Locations(string token, string subscriptionId)
        {
            var body = await Request(token, $"/subscriptions/{subscriptionId}/locations", "2016-06-01");
            var x = JObject.Parse(body);
            return x["value"].Select(_ => (string)_["name"]).ToArray();
        }

        static async Task<Quota[]> ComputeQuota(string token, string subscriptionId, string location)
        {
            Console.WriteLine($"compute {location}");

            var body = await Request(token, $"/subscriptions/{subscriptionId}/providers/Microsoft.Compute/locations/{location}/usages", "2018-04-01"); // "2017-12-01"
            return ParseQuota(body, subscriptionId, location, "compute");
        }

        static async Task<Quota[]> NetworkQuota(string token, string subscriptionId, string location)
        {
            Console.WriteLine($"network {location}");

            var body = await Request(token, $"/subscriptions/{subscriptionId}/providers/Microsoft.Network/locations/{location}/usages", "2018-04-01"); // "2017-12-01"
            return ParseQuota(body, subscriptionId, location, "network");
        }
        static Quota[] ParseQuota(string body, string subscriptionId, string location, string prefix)
        {
            if (body == null)
            {
                return new Quota[0];
            }

            var x = JObject.Parse(body);
            return x["value"].Select(_ => new Quota
            {
                SubscriptionID = subscriptionId,
                Location = location,
                Category = prefix,
                Name = (string)_["name"]["value"],
                Current = (string)_["currentValue"],
                Limit = (string)_["limit"]
            }).ToArray();
        }
    }

    public class Quota
    {
        public string SubscriptionID { get; set; }
        public string Location { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string Current { get; set; }
        public string Limit { get; set; }

        public override string ToString() => String.Join("\t", SubscriptionID, Location, Category, Name, Current, Limit);
    }

    class Token
    {
        public string _authority { get; set; }
        public string accessToken { get; set; }
    }

    static class MyExtensions
    {
        public static string ToQueryString(this NameValueCollection nvc)
        {
            StringBuilder sb = new StringBuilder("?");

            bool first = true;

            foreach (string key in nvc.AllKeys)
            {
                foreach (string value in nvc.GetValues(key))
                {
                    if (!first)
                    {
                        sb.Append("&");
                    }

                    sb.AppendFormat("{0}={1}", Uri.EscapeDataString(key), Uri.EscapeDataString(value));

                    first = false;
                }
            }

            return sb.ToString();
        }
    }
}
