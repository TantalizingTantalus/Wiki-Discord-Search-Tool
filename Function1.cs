//-------------------------------------------------------------+
// The purpose of this program is to scrape                    |
// a webpage and use the Discord bot to send the               |
// scraped information to a custom Discord channel.            |
//                                                             |
// For fun, entering '2' while running the program will        |
// grab and display the Merriam Webster's Word of the Day      |
// for the current day. This will be displayed in whatever     |
// channel is specified.                                       |
//-------------------------------------------------------------+

using System.Globalization;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Discord.Net;
using Discord.WebSocket;
using Discord;
using System.Text;
using Discord.Interactions;
using System.Runtime.CompilerServices;
using Discord.Commands;

namespace functiondiscordtest
{
    // Channel ID's to route IMessage
    // Replace existing values with YOUR Discord ChannelID's, be sure your bot is authorized in the server
    public static class ChannelID
    {
        // I have 2 channels the bot posts to

        //Development server
        //public const ulong WordOfTheDay = 1177756368954986637;
        //public const ulong General = 1177585011604594721;



        /* INTERNAL USE ONLY, CLEAR BEFORE COMMIT + PUSH 

        DevelopmentTesting: 
        Word of the Day - 1177756368954986637
        General - 1177585011604594721

        Gulag:
        Word of the Day - 1113258574873894932
        General - 1113258881494298706
        Casual Comrades - 1113259032862543953
         */

        //Gulag Server
        public const ulong WordOfTheDay = 1113258574873894932;
        public const ulong General = 1113259032862543953;
    }

    //ID's used in channel pings
    public static class PingID
    {
        // Replace with your Discord UserID's
        public const ulong CustomUser = 123;
        public const string Everyone = "@everyone";
        public static string None = string.Empty;
    }



    public class Function1
    {
        private DiscordSocketClient _client;


        [FunctionName("Function1")]
        public async Task RunBotAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Info, // Set the log level (you can adjust this)
                MessageCacheSize = 100, // Set the number of messages the client should cache

                // Add other configurations as needed
                // For example:
                // GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
            });
            await _client.LoginAsync(TokenType.Bot, "MTE3NzU4MTgxMDM2OTE3MTUxNg.GYaWNw.XNlKkAiJ3esF43OnUMi8ASypwFtkrm9CG0c5wE");
            await _client.StartAsync();

            _client.Log += Log;

            _client.MessageReceived += HandleCommand;




            await Task.Delay(-1);
        }

        static string Replace(string FullText, string TextToReplace)
        {
            switch (TextToReplace)
            {
                case "em": // Clean and format <em> tags
                    FullText = FullText.Replace($"<{TextToReplace}>", "**");
                    FullText = FullText.Replace($"</{TextToReplace}>", "**");
                    break;
                case "a": // Clean and format <a href""> links
                    FullText = FullText.Replace($"\">", ")[");
                    FullText = FullText.Replace($"</a>", "]");
                    FullText = FullText.Replace($"<a href=\"", "(");
                    string LinkPattern = @"\(([^)]*)\)\[([^)]*)\]";
                    FullText.Replace("\t", " ");
                    Match LinkMatch = Regex.Match(FullText, LinkPattern);
                    while (LinkMatch.Success)
                    {
                        // Full-text format is: [link-url](link-text)
                        FullText = FullText.Replace($"{LinkMatch.Groups[0].Value}", $"[{LinkMatch.Groups[2].Value}]({LinkMatch.Groups[1].Value})");
                        LinkMatch = Regex.Match(FullText, LinkPattern);
                    }
                    break;
                default: // Clean any <> </> tags 
                    FullText = FullText.Replace($"<{TextToReplace}>", "");
                    FullText = FullText.Replace($"</{TextToReplace}>", "");
                    break;
            }

            return FullText;
        }

        // Clean the leftover HTML from the scraped "extracts" 
        static string CleanHTML(string Input, bool ClearTopLine = true)
        {
            Input = Replace(Input, "i");
            Input = Replace(Input, "b");
            Input = Replace(Input, "p");
            Input = Replace(Input, "em");
            Input = Replace(Input, "a");
            if (ClearTopLine)
            {
                int IndexOf = Input.IndexOf("\n");
                if (IndexOf != -1)
                {
                    Input = Input.Substring(IndexOf + 1);
                }
            }
            return Input;
        }

        static async Task<string> GetRandomGifUrl(string apiKey)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                string giphyApiUrl = $"https://api.giphy.com/v1/gifs/random?api_key={apiKey}";

                HttpResponseMessage response = await httpClient.GetAsync(giphyApiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    // Use Newtonsoft.Json for parsing the JSON response
                    var giphyResponse = JsonConvert.DeserializeObject<GiphyResponse>(jsonResponse);

                    // Check if the image URL is available in the response
                    if (giphyResponse?.Data?.Image_Url != null)
                    {
                        return giphyResponse.Data.Image_Url;
                    }
                    else
                    {
                        Console.WriteLine("Error: Image URL not found in the Giphy API response.");
                        return null;
                    }
                }
                else
                {
                    Console.WriteLine($"Error: {response.StatusCode} - {response.ReasonPhrase}");
                    return null;
                }
            }
        }

        // Define a class structure to match the Giphy API response
        public class GiphyResponse
        {
            public GiphyData Data { get; set; }
        }

        public class GiphyData
        {
            [JsonProperty("url")]
            public string Image_Url { get; set; }
        }

        // Send string Message to a channel your bot is authorized in 
        static async Task<bool> SendDiscordMessage(string Message, ISocketMessageChannel targetChannel)
        {
            try
            {
                SocketTextChannel TargetChannel = (SocketTextChannel)targetChannel;
                Console.WriteLine($"Attempting to write to channel {TargetChannel.Id}...");
                // Create bot token
                const string BotToken = @"MTE3NzU4MTgxMDM2OTE3MTUxNg.GYaWNw.XNlKkAiJ3esF43OnUMi8ASypwFtkrm9CG0c5wE";         //Replace with your bot token
                                                                                                                             // Assign ChannelID to send message to
                ulong Id = TargetChannel.Id;
                string GiphyAPIKey = "OeQ7yIiBqJthTrh0hwen8E0Csd78dkWy";

                string BufferGIF = await GetRandomGifUrl(GiphyAPIKey);
                Console.WriteLine($"Random GIF URL: {BufferGIF}");

                //@"https://media.giphy.com/media/tBb19f62NciUiu5q0fu/giphy.gif";
                //https://media.giphy.com/media/tBb19f62NciUiu5q0fu/giphy.gif

                // Send Message to channel
                await TargetChannel.SendMessageAsync($"{PingID.None}\n\n\n{Message}\n\nThanks for following (**{TargetChannel.Name}**) updates!");
                await TargetChannel.SendMessageAsync(BufferGIF);

                // Return success
                return true;
            }
            catch
            {

                // Return failure
                return false;
            }

        }

        // Add '+' in place of ' ' in the keyword to perform search
        static async Task<string> CleanSpaces(string KeywordName)
        {
            return KeywordName.Replace(" ", "+");
        }



        private Task Log(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        static string ToTitleCase(string input)
        {
            // Create a TextInfo object for the current culture
            TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;

            // Convert the input string to title case
            return textInfo.ToTitleCase(input);
        }

        private async Task HandleCommand(SocketMessage arg)
        {
            SocketUserMessage message = arg as SocketUserMessage;

            if (message == null || message.Author.IsBot)
            {
                return;
            }

            if (!string.IsNullOrEmpty(message.Content))
            {
                // Cache the message text
                string CachedMsg = message.Content;
                const string CommandName = @"/search";

                const string SearchCommandPattern = $"^({CommandName})\\s+(\\S+)$";
                const string WOTDCommandPattern = @"\/(wotd)";
                const string PoopPattern = @"poop";
                RegexOptions options = RegexOptions.IgnoreCase;

                Match SearchMatch = Regex.Match(CachedMsg, SearchCommandPattern);
                Match WordMatch = Regex.Match(CachedMsg, WOTDCommandPattern);
                Match PoopMatch = Regex.Match(CachedMsg, PoopPattern, options);

                if (PoopMatch.Success)
                {
                    try
                    {
                        if (!(await SendDiscordMessage($"Hehe... your last message contained {PoopMatch.Value}", message.Channel)))
                        {
                            Console.WriteLine("Failed to send poop message...");
                        }

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Caught poop exception: " + ex.ToString());
                    }
                }

                if (SearchMatch.Success)
                {
                    string SearchKey = SearchMatch.Groups[2].Value;
                    SearchKey = SearchKey.Replace("-", " ");
                    SearchKey = ToTitleCase(SearchKey);
                    // process search command

                    try
                    {
                        using (HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                        {
                            // Scrape the webpage and ensure Success
                            string ScraperURL = $"https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&titles={SearchKey}&utf8=1&exintro=1&explaintext=true";
                            HttpResponseMessage Response = await Client.GetAsync(ScraperURL);
                            Response.EnsureSuccessStatusCode();

                            // Parse scraped result
                            string JsonResult = await Response.Content.ReadAsStringAsync();
                            JObject ParsedResult = JObject.Parse(JsonResult);

                            // Create Message to send
                            string Message = $"Failed to grab {SearchKey}.";
                            Message = ParsedResult.ToString();
                            Message = ParsedResult["query"]["pages"].First.First["extract"].ToString();
                            Message = $"**{SearchKey}**:\n" + Message;
                            Message = CleanHTML(Message);

                            // Check if message is less than 1800 characters (Discord's limit is 2000)
                            if (Message.Length < 1700)
                            {
                                if (!(await SendDiscordMessage(Message, message.Channel)))
                                {
                                    Console.WriteLine($"Failed to post message...");
                                }

                                Console.WriteLine($"Posted search to Discord Channel.");
                            }
                            else
                            {
                                // Take first 1800 characters from string to be safe
                                string ModifiedMessage = new string(Message.Take(1700).ToArray());
                                Console.WriteLine(ModifiedMessage.Length);
                                if (!await SendDiscordMessage($"There seems to be quite a bit on **{SearchKey}**, this is all I could pull: \n\n{ModifiedMessage}", message.Channel))
                                {
                                    Console.WriteLine("Failed to send message...");
                                    throw new Exception("Failed to post.");
                                }
                            }

                            // Await next input
                            Console.WriteLine("\n\nMake another search by typing in a keyword, otherwise enter 'quit' or 'quit program'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!await SendDiscordMessage($"Failed to parse your search, try again!\n\nException caught: \n{ex.Message}", message.Channel))
                        {
                            Console.WriteLine("Failed to send message...");
                            throw new Exception("Failed to post.");
                        }
                    }
                }

                if (WordMatch.Success)
                {
                    using (HttpClient client = new HttpClient())
                    {
                        string ScraperURL = "https://www.merriam-webster.com/word-of-the-day";

                        SocketGuild guild = _client.GetGuild(ChannelID.WordOfTheDay); // Replace GUILD_ID with the ID of your guild

                        SocketChannel som = _client.GetChannel(ChannelID.WordOfTheDay);

                        // Get the channel using the ID
                        SocketTextChannel channel = (SocketTextChannel)som;

                        // Prepare search pattern with scraped result
                        string WordOfTheDayPattern = @"<title>(Word of the Day):\s(\w+)";
                        string Html = await client.GetStringAsync(ScraperURL);
                        Match WOTDMatch = Regex.Match(Html, WordOfTheDayPattern);

                        // Search for word of the day 
                        if (WOTDMatch.Success)
                        {
                            string WOTDDefinitionPattern = $"<p><em>{WOTDMatch.Groups[2].Value}<\\/em>\\s.*?(.*?)<\\/p>";
                            Match DefinitionMatch = Regex.Match(Html, WOTDDefinitionPattern);

                            // Search for word of the day definition
                            if (DefinitionMatch.Success)
                            {
                                // Format word of the day message
                                string Message = $"**{WOTDMatch.Groups[1].Value}:** {WOTDMatch.Groups[2].Value}" + $"\n**Definition:** *{DefinitionMatch.Groups[1].Value}*";
                                Message = CleanHTML(Message, false);

                                // Send word of the day message
                                if (!await SendDiscordMessage(Message, message.Channel))
                                {
                                    Console.WriteLine("Failed to post the word of the day...");
                                    throw new Exception("Failed to post.");
                                }
                                Console.WriteLine($"Successfully posted word of the day to discord channel.");
                            }
                        }
                        // Await next input
                        Console.WriteLine("\n\nMake another search by typing in a keyword, otherwise enter 'quit' or 'quit program'.");
                    }
                }
            }
        }
    }
}








