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

using System.Text.Json;
using Newtonsoft.Json;
using System.Net;
using System;
using Newtonsoft.Json.Linq;
using Discord.Net;
using Discord.WebSocket;
using Discord;
using System.Text.RegularExpressions;
using System.Text;


// Channel ID's to route IMessage
// Replace existing values with YOUR Discord ChannelID's, be sure your bot is authorized in the server
public static class ChannelID
{
    // I have 2 channels the bot posts to
    public const ulong WordOfTheDay = 123;     
    public const ulong General = 1234;
}

//ID's used in channel pings
public static class PingID
{
    // Replace with your Discord UserID's
    public const ulong CustomUser = 123;     
    public const string Everyone = "@everyone";
}

class Program
{
    // Used to agregate Replace() calls from CleanHTML()
    static string Replace(string FullText, string TextToReplace)
    {
        switch(TextToReplace)
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
                    string temp = LinkMatch.Groups[1].Value;
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
    static string CleanHTML(string Input)
    {
        Input = Replace(Input, "i");
        Input = Replace(Input, "b");
        Input = Replace(Input, "p");
        Input = Replace(Input, "em");
        Input = Replace(Input, "a");
        int IndexOf = Input.IndexOf("\n");
        if(IndexOf != -1)
        {
            Input = Input.Substring(IndexOf + 1);
        }
        return Input;
    }

    // Send string Message to a channel your bot is authorized in 
    static async Task<bool> SendDiscordMessage(string Message, ulong ChannelID = ChannelID.General)
    {
        try
        {
            // Create bot token
            const string BotToken = @"YOUR BOT TOKEN HERE";         //Replace with your bot token

            // Set up Discord client
            DiscordSocketClient Client = new DiscordSocketClient();
            await Client.LoginAsync(TokenType.Bot, BotToken);
            await Client.StartAsync();

            // Assign ChannelID to send message to
            ulong Id = ChannelID;
            var Channel = await Client.GetChannelAsync(Id) as IMessageChannel;

            // Send Message to channel
            await Channel!.SendMessageAsync($"{PingID.Everyone}\n\n\n" + Message + $"\n\nThanks for following (**{Channel.Name}**) updates!");

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

    // Add ' ' in place of '+' in the keyword to display in Discord
    static async Task<string> RemovePlusSign(string KeywordName)
    {
        return KeywordName.Replace("+", " "); 
    }

    // Get and format input for search
    static async Task<string> GetInput()
    {
        string? Input = Console.ReadLine();
        Input = await CleanSpaces(Input);
        return Input;
    }

    // Main logic
    static async Task Main()
    {
        // Store the input data
        string? KeywordName = String.Empty;

        // Keyword to enter to display the Merriam Webster's Word of the Day
        string WOTDClause = "2";

        // Main loop entry
        try
        {
            // Display menu and await input
            Console.WriteLine($"Enter any single word to begin, or enter '{WOTDClause}' at any time to view the Merriam Webster's Word of the Day.\n");
            KeywordName = await GetInput();
            string ScraperURL = $"https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&titles={KeywordName}&utf8=1&exintro=1&explaintext=true";

            // Break if 'quit' or 'quit program' is entered
            while (!KeywordName.ToLower().Equals("quit program") || !KeywordName.ToLower().Equals("quit"))
            {
                if(KeywordName.Equals(WOTDClause))
                {
                    ScraperURL = "https://www.merriam-webster.com/word-of-the-day";
                    using (HttpClient client = new HttpClient())
                    {
                        // Prepare search pattern with scraped result
                        string WordOfTheDayPattern = @"<title>(Word of the Day):\s(\w+)";
                        string Html = await client.GetStringAsync(ScraperURL);
                        Match WOTDMatch = Regex.Match(Html, WordOfTheDayPattern);

                        // Search for word of the day 
                        if(WOTDMatch.Success)
                        { 
                            string WOTDDefinitionPattern = $"<p><em>{WOTDMatch.Groups[2].Value}<\\/em>\\s.*?(.*?)<\\/p>";
                            Match DefinitionMatch = Regex.Match(Html, WOTDDefinitionPattern);

                            // Search for word of the day definition
                            if(DefinitionMatch.Success)
                            {
                                // Format word of the day message
                                string Message = $"**{WOTDMatch.Groups[1].Value}:** {WOTDMatch.Groups[2].Value}" + $"\n**Definition:** *{DefinitionMatch.Groups[1].Value}*";
                                Message = CleanHTML(Message);

                                // Send word of the day message
                                if(!await SendDiscordMessage(Message, ChannelID.WordOfTheDay))
                                {
                                    Console.WriteLine("Failed to post the word of the day...");
                                    throw new Exception("Failed to post.");
                                } 
                                Console.WriteLine($"Successfully posted word of the day to discord channel.");
                            }
                        }

                        // Await next input
                        Console.WriteLine("\n\nMake another search by typing in a keyword, otherwise enter 'quit' or 'quit program'.");
                        KeywordName = await GetInput();
                        ScraperURL = $"https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&titles={KeywordName}&utf8=1&exintro=1&explaintext=true";
                    }
                }
                else
                {
                    using (HttpClient Client = new HttpClient())
                    {
                        // Scrape the webpage and ensure Success
                        HttpResponseMessage Response = await Client.GetAsync(ScraperURL);
                        Response.EnsureSuccessStatusCode();

                        // Parse scraped result
                        string JsonResult = await Response.Content.ReadAsStringAsync();
                        JObject ParsedResult = JObject.Parse(JsonResult);

                        // Format Keyword for Message
                        KeywordName = await RemovePlusSign(KeywordName);

                        // Create Message to send
                        string Message = $"Failed to grab {KeywordName}.";
                        Message = ParsedResult.ToString();
                        Message = ParsedResult["query"]["pages"].First.First["extract"].ToString();
                        Message = $"**{KeywordName}**:\n" + Message;
                        Message = CleanHTML(Message);

                        // Check if message is less than 1800 characters (Discord's limit is 2000)
                        if (Message.Length < 1800)
                        {
                            if(!await SendDiscordMessage(Message))
                            {
                                Console.WriteLine($"Failed to post message...");
                                throw new Exception("Failed to post.");
                            }

                            Console.WriteLine($"Posted search to Discord Channel.");
                        }
                        else
                        {
                            // Take first 1800 characters from string to be safe
                            string ModifiedMessage = new string(Message.Take(1800).ToArray());
                            Console.WriteLine(ModifiedMessage.Length);
                            if(!await SendDiscordMessage($"There seems to be quite a bit on **{KeywordName}**, this is all I could pull: \n\n{ModifiedMessage}"))
                            {
                                Console.WriteLine("Failed to send message...");
                                throw new Exception("Failed to post.");
                            }
                        }

                        // Await next input
                        Console.WriteLine("\n\nMake another search by typing in a keyword, otherwise enter 'quit' or 'quit program'.");
                        KeywordName = await GetInput();
                        ScraperURL = $"https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&titles={KeywordName}&utf8=1&exintro=1&explaintext=true";
                    }
                }
            }
        }
        catch (Exception Ex)
        {
            Console.WriteLine($"Ran into problems, response message: {Ex.Message}");
            if(await SendDiscordMessage($"Failed to execute command, something went wrong try another search."))
            {
                Console.WriteLine("Error posted to channel.");
            }
            return;
        }
    }
}



