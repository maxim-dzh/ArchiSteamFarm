using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;
using System.IO;
using System.Threading;
using System.Reflection.Metadata;
using AngleSharp;
using ArchiSteamFarm.Steam.Integration;
using Microsoft.VisualBasic;

namespace ASFFriendManager;

// In order for your plugin to work, it must export generic ASF's IPlugin interface
[Export(typeof(IPlugin))]

// Your plugin class should inherit the plugin interfaces it wants to handle
// If you do not want to handle a particular action (e.g. OnBotMessage that is offered in IBotMessage), it's the best idea to not inherit it at all
// This will keep your code compact, efficient and less dependent. You can always add additional interfaces when you'll need them, this example project will inherit quite a bit of them to show you potential usage
[SuppressMessage("ReSharper", "MemberCanBeFileLocal")]
internal sealed partial class ASFFriendManagerPlugin : IBot, IBotCommand2, IBotConnection, IBotFriendRequest, IBotMessage, IBotModules, IBotTradeOffer2
{
	// This is used for identification purposes, typically you want to use a friendly name of your plugin here, such as the name of your main class
	// Please note that this property can have direct dependencies only on structures that were initialized by the constructor, as it's possible to be called before OnLoaded() takes place
	[JsonInclude]
	[Required]
	public string Name => nameof(ASFFriendManagerPlugin);

	// This will be displayed to the user and written in the log file, typically you should point it to the version of your library, but alternatively you can do some more advanced logic if you'd like to
	// Please note that this property can have direct dependencies only on structures that were initialized by the constructor, as it's possible to be called before OnLoaded() takes place
	[JsonInclude]
	[Required]
	public Version Version => typeof(ASFFriendManagerPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	// Plugins can expose custom properties for our GET /Api/Plugins API call, simply annotate them with [JsonProperty] (or keep public)
	[JsonInclude]
	[Required]
	public bool CustomIsEnabledField { get; private init; } = true;

	// This method is called when unknown command is received (starting with CommandPrefix)
	// This allows you to recognize the command yourself and implement custom commands
	// Keep in mind that there is no guarantee what is the actual access of steamID, so you should do the appropriate access checking yourself
	// You can use either ASF's default functions for that, or implement your own logic as you please
	// Since ASF already had to do initial parsing in order to determine that the command is unknown, args[] are splitted using standard ASF delimiters
	// If by any chance you want to handle message in its raw format, you also have it available, although for usual ASF pattern you can most likely stick with args[] exclusively. The message has CommandPrefix already stripped for your convenience
	// If you do not recognize the command, just return null/empty and allow ASF to gracefully return "unknown command" to user on usual basis
	public async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0)
	{
		// In comparison with OnBotMessage(), we're using asynchronous CatAPI call here, so we declare our method as async and return the message as usual
		// Notice how we handle access here as well, it'll work only for FamilySharing+
		switch (args[0].ToUpperInvariant())
		{
			case "ADDFRIENDBYINVITE" when access >= EAccess.FamilySharing:
				if (args.Length < 3)
				{
					return "args count is wrong";
				}
				string botNames = args[1];
				if (string.IsNullOrEmpty(botNames))
				{
					return "bot names are empty";
				}
				HashSet<Bot>? bots = Bot.GetBots(botNames);
				if (bots == null || bots.Count == 0)
				{
					return "fail";
				}
				string friendSteamID = "false";
				//IList<(bool Success, string? Token, string Message)> results = await Utilities.InParallel(bots.Select(static bot => bot.Actions.GenerateTwoFactorAuthenticationToken())).ConfigureAwait(false);
				IList<(bool success, string steamID)> results = await Utilities.InParallel(bots.Select(bot => AddToFriends(bot, args[2]))).ConfigureAwait(false);
				for (int i = 0; i < results.Count; i++)
				{
					friendSteamID = results[i].steamID;
					if (!results[i].success)
					{
						return "false";
					}
				}
				await Task.Delay(5).ConfigureAwait(false);
				return friendSteamID;
			default:
				return null;
		}
	}

	// This method is called when bot is destroyed, e.g. on config removal
	// You should ensure that all of your references to this bot instance are cleared - most of the time this is anything you created in OnBotInit(), including deep roots in your custom modules
	// This doesn't have to be done immediately (e.g. no need to cancel existing work), but it should be done in timely manner when everything is finished
	// Doing so will allow the garbage collector to dispose the bot afterwards, refraining from doing so will create a "memory leak" by keeping the reference alive
	public Task OnBotDestroy(Bot bot) => Task.CompletedTask;

	// This method is called when bot is disconnected from Steam network, you may want to use this info in some kind of way, or not
	// ASF tries its best to provide logical reason why the disconnection has happened, and will use EResult.OK if the disconnection was initiated by us (e.g. as part of a command)
	// Still, you should take anything other than EResult.OK with a grain of salt, unless you want to assume that Steam knows why it disconnected us (hehe, you bet)
	public Task OnBotDisconnected(Bot bot, EResult reason) => Task.CompletedTask;

	// This method is called when bot receives a friend request or group invite that ASF isn't willing to accept
	// It allows you to generate a response whether ASF should accept it (true) or proceed like usual (false)
	// If you wanted to do extra filtering (e.g. friend requests only), you can interpret the steamID as SteamID (SteamKit2 type) and then operate on AccountType
	// As an example, we'll run a trade bot that is open to all friend/group invites, therefore we'll accept all of them here
	public Task<bool> OnBotFriendRequest(Bot bot, ulong steamID) => Task.FromResult(true);

	// This method is called at the end of Bot's constructor
	// You can initialize all your per-bot structures here
	// In general you should do that only when you have a particular need of custom modules or alike, since ASF's plugin system will always provide bot to you as a function argument
	public Task OnBotInit(Bot bot)
	{
		// Apart of those two that are already provided by ASF, you can also initialize your own logger with your plugin's name, if needed
		bot.ArchiLogger.LogGenericInfo($"Our bot named {bot.BotName} has been initialized, and we're letting you know about it from our {nameof(ASFFriendManagerPlugin)}!");
		ASF.ArchiLogger.LogGenericWarning("In case we won't have a bot reference or have something process-wide to log, we can also use ASF's logger!");

		return Task.CompletedTask;
	}

	// This method, apart from being called during bot modules initialization, allows you to read custom bot config properties that are not recognized by ASF
	// Thanks to that, you can extend default bot config with your own stuff, then parse it here in order to customize your plugin during runtime
	// Keep in mind that, as noted in the interface, additionalConfigProperties can be null if no custom, unrecognized properties are found by ASF, you should handle that case appropriately
	// Also keep in mind that this function can be called multiple times, e.g. when user edits their bot configs during runtime
	// Take a look at OnASFInit() for example parsing code
	public async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null)
	{
		// For example, we'll ensure that every bot starts paused regardless of Paused property, in order to do this, we'll just call Pause here in InitModules()
		// Thanks to the fact that this method is called with each bot config reload, we'll ensure that our bot stays paused even if it'd get unpaused otherwise
		bot.ArchiLogger.LogGenericInfo("Pausing this bot as asked from the plugin");
		await bot.Actions.Pause(true).ConfigureAwait(false);
	}

	// This method is called when the bot is successfully connected to Steam network and it's a good place to schedule any on-connected tasks, as AWH is also expected to be available shortly
	public Task OnBotLoggedOn(Bot bot) => Task.CompletedTask;

	// This method is called when bot receives a message that is NOT a command (in other words, a message that doesn't start with CommandPrefix)
	// Normally ASF entirely ignores such messages as the program should not respond to something that isn't recognized
	// Therefore this function allows you to catch all such messages and handle them yourself
	// Keep in mind that there is no guarantee what is the actual access of steamID, so you should do the appropriate access checking yourself
	// You can use either ASF's default functions for that, or implement your own logic as you please
	// If you do not intend to return any response to user, just return null/empty and ASF will proceed with the silence as usual
	public Task<string?> OnBotMessage(Bot bot, ulong steamID, string message)
	{
		// Normally ASF will expect from you async-capable responses, such as Task<string>. This allows you to make your code fully asynchronous which is a core foundation on which ASF is built upon
		// Since in this method we're not doing any async stuff, instead of defining this method as async (pointless), we just need to wrap our responses in Task.FromResult<>()
		if (Bot.BotsReadOnly == null)
		{
			throw new InvalidOperationException(nameof(Bot.BotsReadOnly));
		}

		// As a starter, we can for example ignore messages sent from our own bots, since otherwise they can run into a possible infinite loop of answering themselves
		if (Bot.BotsReadOnly.Values.Any(existingBot => existingBot.SteamID == steamID))
		{
			return Task.FromResult<string?>(null);
		}

		// If this message doesn't come from one of our bots, we can reply to the user in some pre-defined way
		bot.ArchiLogger.LogGenericTrace("Hey boss, we got some unknown message here!");

		return Task.FromResult<string?>("I didn't get that, did you mean to use a command?");
	}

	// This method is called when bot receives a trade offer that ASF isn't willing to accept (ignored and rejected trades)
	// It allows you not only to analyze such trades, but generate a response whether ASF should accept it (true), or proceed like usual (false)
	// Thanks to that, you can implement custom rules for all trades that aren't handled by ASF, for example cross-set trading on your own custom rules
	// You'd implement your own logic here, as an example we'll allow all trades to be accepted if the bot's name starts from "TrashBot"
	public Task<bool> OnBotTradeOffer(Bot bot, TradeOffer tradeOffer, ParseTradeResult.EResult asfResult) => Task.FromResult(bot.BotName.StartsWith("TrashBot", StringComparison.OrdinalIgnoreCase));

	// This is the earliest method that will be called, right after loading the plugin, long before any bot initialization takes place
	// It's a good place to initialize all potential (non-bot-specific) structures that you will need across lifetime of your plugin, such as global timers, concurrent dictionaries and alike
	// If you do not have any global structures to initialize, you can leave this function empty
	// At this point you can access core ASF's functionality, such as logging, but more advanced structures (like ASF's WebBrowser) will be available in OnASFInit(), which itself takes place after every plugin gets OnLoaded()
	// Typically you should use this function only for preparing core structures of your plugin, and optionally also sending a message to the user (e.g. support link, welcome message or similar), ASF-specific things should usually happen in OnASFInit()
	public Task OnLoaded()
	{
		ASF.ArchiLogger.LogGenericInfo($"Hey! Thanks for checking if our example plugin works fine, this is a confirmation that indeed {nameof(OnLoaded)}() method was called!");
		ASF.ArchiLogger.LogGenericInfo("Good luck in whatever you're doing!");

		return Task.CompletedTask;
	}

	internal static async Task<(bool, string)> AddToFriends(Bot bot, string inviteLink)
	{
		bot.ArchiLogger.LogGenericError($"Invite link is {inviteLink}");
		Uri inviteURI = new(inviteLink);
		HtmlDocumentResponse? response = await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(inviteURI, maxTries: 2).ConfigureAwait(false);
		if (response == null || response.Content == null || response.StatusCode != HttpStatusCode.OK)
		{
			bot.ArchiLogger.LogGenericError($"Unable to load {inviteLink} page");
			return (false, "");
		}

		//using (FileStream stream = new("resp.html", FileMode.Create, FileAccess.Write))
		//using (StreamWriter writer = new(stream)) {
		//	await response.Content.ToHtmlAsync(writer).ConfigureAwait(false);
		//}

		AngleSharp.Dom.IElement? aTag = response.Content.QuerySelector(".persona_level_btn");
		if (aTag == null)
		{
			bot.ArchiLogger.LogGenericError($"Unable to get profile link for {inviteLink}. Selector \"a\" is null");
			return (false, "");
		}
		string? profileLink = aTag.GetAttribute("href");
		if (string.IsNullOrWhiteSpace(profileLink))
		{
			bot.ArchiLogger.LogGenericError($"Unable to get profile link for {inviteLink}. Attribute \"href\" is empty");
			return (false, "");
		}
		Regex regex = SteamIDRegex();
		Match match = regex.Match(profileLink);
		if (!match.Success || match.Groups.Count < 2)
		{
			bot.ArchiLogger.LogGenericError($"Failed to extract steamID from {profileLink} link");
			return (false, "");
		}
		string friendSteamID = match.Groups[1].Value;
		Regex inviteRegex = InviteTokenRegex();
		Match inviteRegexMatch = inviteRegex.Match(inviteLink);
		if (!inviteRegexMatch.Success || inviteRegexMatch.Groups.Count < 2)
		{
			bot.ArchiLogger.LogGenericError($"Failed to extract invite token from {inviteLink} link");
			return (false, "");
		}
		string inviteToken = inviteRegexMatch.Groups[1].Value;
		string? sessionID = bot.ArchiWebHandler.WebBrowser.CookieContainer.GetCookieValue(ArchiWebHandler.SteamCommunityURL, "sessionid");
		if (string.IsNullOrEmpty(sessionID))
		{
			bot.ArchiLogger.LogGenericError($"sessionID is not set for {ArchiWebHandler.SteamCommunityURL}");
			return (false, "");
		}

		Uri addFriendUri = new($"https://steamcommunity.com/invites/ajaxredeem?sessionid={sessionID}&steamid_user={friendSteamID}&invite_token={inviteToken}");
		HtmlDocumentResponse? addFriendResponse = await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(addFriendUri, maxTries: 2).ConfigureAwait(false);
		if (addFriendResponse == null)
		{
			bot.ArchiLogger.LogGenericError($"Unable to load {addFriendUri} link");
			return (false, "");
		}
		if (addFriendResponse.Content == null || addFriendResponse.StatusCode != HttpStatusCode.OK)
		{
			bot.ArchiLogger.LogGenericError($"Unable to load {addFriendUri} link. Status code {addFriendResponse.StatusCode}");
			return (false, "");
		}
		bot.ArchiLogger.LogGenericInfo($"add to friends operation succeed! {inviteLink}");
		return (true, friendSteamID);
	}

	[GeneratedRegex("^https://steamcommunity\\.com/profiles/([0-9]+)\\/[a-zA-Z]+\\/?")]
	private static partial Regex SteamIDRegex();

	[GeneratedRegex("^https:\\/\\/s\\.team\\/p\\/[a-zA-Z-]+\\/([a-zA-Z]+)\\/?")]
	private static partial Regex InviteTokenRegex();
}


