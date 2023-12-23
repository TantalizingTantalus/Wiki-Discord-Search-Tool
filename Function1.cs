//-------------------------------------------------------------+
// The purpose of this program is to scrape                    |
// a webpage and use the Discord bot to send the               |
// scraped information to a custom Discord channel.            |
//                                                             |
// Currently, you enter your Discord channel ID and bot token  |
// and compile (Alternatively I set my bot up on Azure         |
// Functions to run whenever I ping the server).               |
//                                                             |
// Use the commands "/search {search term}" to scrape          |
// Wikipedia using the OpenSearch REST API for the relevant    |
// term.                                                       |
//                                                             |
// Use the command "/wotd" to send the Merriam Webster's Word  |
// of the Day to a specified Discord server                    |
//-------------------------------------------------------------+

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Net;
using Microsoft.Azure.WebJobs.Host;
using Discord.Net;
using Discord.WebSocket;
using Discord;
using System.Text;
using Discord.Interactions;
using System.Runtime.CompilerServices;
using Discord.Commands;
using Microsoft.Azure.WebJobs.Host.Bindings;
using System.Threading;
using System.Collections.Generic;

namespace SmellyFeet
{
    
    // Channel ID's to route IMessage
    // Replace existing values with YOUR Discord ChannelID's, be sure your bot is authorized in the server
    public static class ChannelID
    {
        public const ulong General = 0;   // Your channel Id here
        public const ulong WordOfTheDay = 0;    // Extra channels just add properties
    }

    //ID's used in channel pings
    public static class PingID
    {
        // Replace with your Discord UserID's
        public const ulong CustomUser = 123;
        public const string Everyone = "@everyone";
        public static string None = string.Empty;
    }

    public static class BotObject
    {
        public static string BOT_TOKEN = "Your Bot Token Here";
    }

    public class WordOfTheDayBlob
    {
        public List<string> SearchKey { get; set; }
    }



    public class Function1
    {
        private static DiscordSocketClient _client;

        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Info, // Set the log level (you can adjust this)
                MessageCacheSize = 100, // Set the number of messages the client should cache

                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
            });

            _client.Log += Log;
            _client.Ready += OnReady;

            await _client.LoginAsync(TokenType.Bot, BotObject.BOT_TOKEN);
            await _client.StartAsync();

            _client.MessageReceived += HandleCommand;


            
            return new OkObjectResult("Function Completed Successfully");
           
        }

        public static async Task OnReady()
        {
            await SendDiscordMessage("I'm Awake!");
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

        static async Task<string> GetRandomGifUrl( string Keyword = null)
        {
            const string apiKey = "OeQ7yIiBqJthTrh0hwen8E0Csd78dkWy";
            using (HttpClient httpClient = new HttpClient())
            {
                string giphyApiUrl = $"https://api.giphy.com/v1/gifs/random?api_key={apiKey}";
                if (Keyword != null)
                {
                    giphyApiUrl = $"https://api.giphy.com/v1/gifs/search?api_key={apiKey}&q={Keyword}&limit=50";
                }
                

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
        static async Task<bool> SendDiscordMessage(string Message, ISocketMessageChannel targetChannel, bool CleanHtml = true, string RandomGIFInfluence = null)
        {
            try
            {
                if(CleanHtml)
                {
                    Message = CleanHTML(Message, false);
                }
                SocketTextChannel TargetChannel = (SocketTextChannel)targetChannel;
                Console.WriteLine($"Attempting to write to channel {TargetChannel.Id}...");
                
                string BufferGIF = await GetRandomGifUrl();
                if (RandomGIFInfluence != null)
                {
                    BufferGIF = await GetRandomGifUrl(RandomGIFInfluence);
                }
                
                Console.WriteLine($"Random GIF URL: {BufferGIF}");

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

        static async Task<bool> SendDiscordMessage(string Message, ulong TargetChannel = ChannelID.General, bool UseGIF = false)
        {
            try
            {
                
                Console.WriteLine($"Attempting to write to channel {_client.GetChannel(TargetChannel)}...");
                ulong Id = TargetChannel;

                string BufferGIF = await GetRandomGifUrl();

                Console.WriteLine($"Random GIF URL: {BufferGIF}");


                //dont know why this is null, possibly the "as sockettextchannel" cast??
                SocketTextChannel SocketChannel = (SocketTextChannel)_client.GetChannel(Id);

                // Send Message to channel
                await SocketChannel.SendMessageAsync($"{PingID.None}\n\n\n{Message}\n\nThanks for following (**{_client.GetChannel(Id).ToString()}**) updates!");

                if(UseGIF) {
                    await SocketChannel.SendMessageAsync(BufferGIF);
                }

                // Return success
                return true;
            }
            catch
            {

                // Return failure
                return false;
            }

        }

        private static Task Log(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        static string ToTitleCase(string input)
        {
            // Create a TextInfo object for the current culture
            TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;

            // Convert the input string to title case
            return textInfo.ToTitleCase(input.ToLower());
        }

        private static async Task HandleCommand(SocketMessage arg)
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

                const string SearchCommandPattern = $"^({CommandName})(.*)$";
                const string WOTDCommandPattern = @"\/(wotd)";
                const string RandomCommandPattern = $"(?i)^\\/(random)\\s(.*)$";
                const string PoopPattern = @"poop";
                RegexOptions options = RegexOptions.IgnoreCase;
                Match PoopMatch = Regex.Match(CachedMsg, PoopPattern, options);
                Match SearchMatch = Regex.Match(CachedMsg, SearchCommandPattern, options);
                Match WordMatch = Regex.Match(CachedMsg, WOTDCommandPattern, options);
                Match RandomMatch = Regex.Match(CachedMsg, RandomCommandPattern, options);

                if(RandomMatch.Success)
                {
                    try
                    {
                        if (RandomMatch.Groups[2].Value.ToLower().Equals("gif"))
                        {
                            string RandomGIF = await GetRandomGifUrl();
                            if (!(await SendDiscordMessage($"Here's a random GIF:\n\n{RandomGIF}", message.Channel)))
                            {
                                Console.WriteLine("Failed to send poop message...");
                            }
                        }
                        
                       

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Caught poop exception: " + ex.ToString());
                    }
                }

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
                    // process search command

                    try
                    {
                        using (HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                        {
                            // Scrape the webpage and ensure Success
                            string ScraperURL = $"https://en.wikipedia.org/w/api.php?action=opensearch&format=json&search={SearchKey}&utf8=1&exintro=1&explaintext=true";
                            
                            HttpResponseMessage Response = await Client.GetAsync(ScraperURL);
                            Response.EnsureSuccessStatusCode();

                            // Parse scraped result
                            string JsonResult = await Response.Content.ReadAsStringAsync();


                            var WTDBlob = JsonConvert.DeserializeObject(JsonResult);
                            
                            if (WTDBlob != null)
                            {
                                JArray JSomethign = (JArray)WTDBlob;
                                if(JSomethign.Any() )
                                {
                                    JToken newtext = JSomethign[1][0];
                                    SearchKey = newtext.ToString();
                                }
                                
                            }
                           
                            //List<JValue> temp = WTDBlob;
                            //string TempToken = temp[1];
                            //SearchKey = WTDBlob.;

                            ScraperURL = $"https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&titles={SearchKey}&utf8=1&exintro=1&explaintext=true";

                            Response = await Client.GetAsync(ScraperURL);
                            Response.EnsureSuccessStatusCode();

                            // Parse scraped result
                            JsonResult = await Response.Content.ReadAsStringAsync();

                            JObject ParsedResult = JObject.Parse(JsonResult);

                            Response = await Client.GetAsync(ScraperURL);
                            Response.EnsureSuccessStatusCode();

                            // Parse scraped result
                            JsonResult = await Response.Content.ReadAsStringAsync();

                            // Create Message to send
                            string Message = $"Failed to grab {SearchKey}.";
                            Message = ParsedResult.ToString();
                            Message = ParsedResult["query"]["pages"].First.First["extract"].ToString();
                            Message = $"**{SearchKey}**:\n" + Message;
                            
                            Message = CleanHTML(Message);

                            // Check if message is less than 1800 characters (Discord's limit is 2000)
                            if (Message.Length < 1700)
                            {
                                if (!(await SendDiscordMessage($"Here is your search on **{SearchKey}**:\n\n" + Message, message.Channel)))
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
                                if (!await SendDiscordMessage($"There seems to be quite a bit on **{SearchKey}**, this is all I could pull: \n\n{ModifiedMessage}", message.Channel, false))
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
                        if (!await SendDiscordMessage($"Failed to parse your search ({SearchKey}), try again!\n\n**Error Message**: \n{ex.Message}", message.Channel))
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


                        SocketChannel som = _client.GetChannel(ChannelID.WordOfTheDay);

                        // Get the channel using the ID
                        SocketTextChannel channel = (SocketTextChannel)som;

                        // Prepare search pattern with scraped result
                        string WordOfTheDayPattern = @"<title>(Word of the Day):\s(\w+)";
                        string Html = await client.GetStringAsync(ScraperURL);

                        // Search for word of the day 
                        Match WOTDMatch = Regex.Match(Html, WordOfTheDayPattern, options);
                        if (WOTDMatch.Success)
                        {
                            string WordOfTheDay = WOTDMatch.Groups[2].Value;
                            string WOTDDefinitionPattern = @"<h2>What It Means<\/h2>(\s.*)";
                            Match DefinitionMatch = Regex.Match(Html, WOTDDefinitionPattern, options);

                            // Search for word of the day definition
                            if (DefinitionMatch.Success)
                            {
                                string WordDefinition = DefinitionMatch.Groups[1].Value;
                                WordDefinition = WordDefinition.Replace("\n", "");
                                WordDefinition = WordDefinition.Trim();

                                // Format word of the day message with definition
                                string Message = $"\n**{WOTDMatch.Groups[1].Value}:**\n*{WordOfTheDay}*\n" + $"\n**Definition:**\n*{WordDefinition}*";
                                
                                Html = Html.Replace(DefinitionMatch.Value, " ");

                                string SentencePattern = $"<p>\\/\\/(.*<em>{WordOfTheDay.ToLower()}<\\/em>.*)<\\/p>";

                                Match UseMatch = Regex.Match(Html, SentencePattern, options);
                                if(UseMatch.Success)
                                {
                                    string WordSentence = UseMatch.Groups[1].Value;
                                    WordSentence = WordSentence.Trim();
                                    Message += $"\n\n**Use** **{WordOfTheDay.ToLower()}** in a sentence:\n*{WordSentence}*";
                                }

                                
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
