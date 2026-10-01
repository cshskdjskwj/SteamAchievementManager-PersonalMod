/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SAM.Game
{
    /// <summary>
    /// Global achievement unlock rates ("Steam 全球解锁率").
    ///
    /// The primary source is the Steam client itself
    /// (ISteamUserStats013::RequestGlobalAchievementPercentages), which answers through the
    /// GameAchievementData callback (id 1102) and is exactly the data steamdb.info displays.
    /// The public Steam Web API is used as a fallback when the callback never arrives.
    /// </summary>
    internal static class GlobalAchievementPercentages
    {
        private const string WebApiUrl =
            "https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={0}";

        private static bool _TlsInitialized;

        private static void EnsureModernTls()
        {
            if (_TlsInitialized == true)
            {
                return;
            }

            // .NET Framework defaults to old protocols; the Steam Web API needs TLS 1.2+.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
                // older runtimes ignore this; the request will fail and be reported normally
            }

            _TlsInitialized = true;
        }

        [DataContract]
        private sealed class WebApiResponse
        {
            [DataMember(Name = "achievementpercentages")]
            public AchievementPercentagesContainer Container { get; set; }
        }

        [DataContract]
        private sealed class AchievementPercentagesContainer
        {
            [DataMember(Name = "achievements")]
            public AchievementPercentage[] Achievements { get; set; }
        }

        [DataContract]
        private sealed class AchievementPercentage
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "percent")]
            public double Percent { get; set; }
        }

        /// <summary>
        /// Downloads global unlock percentages from the Steam Web API.
        /// </summary>
        public static Dictionary<string, double> FetchFromWebApi(long gameId)
        {
            EnsureModernTls();

            string url = string.Format(CultureInfo.InvariantCulture, WebApiUrl, gameId);

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.UserAgent = "SAM.Game";

            string json;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                json = reader.ReadToEnd();
            }

            if (string.IsNullOrEmpty(json) == true)
            {
                throw new InvalidOperationException("Steam Web API returned an empty response.");
            }

            var serializer = new DataContractJsonSerializer(typeof(WebApiResponse));
            WebApiResponse parsed;
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            {
                parsed = (WebApiResponse)serializer.ReadObject(stream);
            }

            var percentages = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var achievements = parsed?.Container?.Achievements;
            if (achievements == null)
            {
                return percentages;
            }

            foreach (var achievement in achievements)
            {
                if (string.IsNullOrEmpty(achievement?.Name) == true)
                {
                    continue;
                }

                percentages[achievement.Name] = achievement.Percent;
            }

            return percentages;
        }
    }
}
