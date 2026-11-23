using System.Text.RegularExpressions;
using Steamworks;

// Publishes the Simplified Chinese title and description for the existing Workshop item
// through ISteamUGC::SetItemUpdateLanguage. The official uploader cannot do this: it only
// writes one language into the item, so the per-language tab stays empty.
//
// Uses Steamworks.NET (the same assembly the game ships with) instead of hand written
// P/Invoke, because the native init path it takes differs from the public SteamAPI_InitFlat
// wrapper and is the one that actually connects to the client.
internal static class Program
{
    private const uint AppId = 2868840;
    private const ulong ItemId = 3843992717;
    private const string Language = "schinese";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length < 2)
        {
            Console.WriteLine("usage: WorkshopLocalizer <localization-zhs.md> <change note>");
            return 2;
        }

        string markdown = File.ReadAllText(args[0]);
        var blocks = Regex.Matches(markdown, "```[^\n]*\n(.*?)```", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(b => b.Length > 0)
            .ToList();
        // The file also documents the update flow in a fenced block, so pick the blocks after the
        // "## 标题" and "## 描述" headings instead of trusting their order.
        int titleHeading = markdown.IndexOf("## 标题", StringComparison.Ordinal);
        int descHeading = markdown.IndexOf("## 描述", StringComparison.Ordinal);
        if (titleHeading < 0 || descHeading < 0 || descHeading < titleHeading)
        {
            Console.WriteLine("could not find the '## 标题' and '## 描述' headings in " + args[0]);
            return 2;
        }
        var titleBlocks = Regex.Matches(markdown.Substring(titleHeading, descHeading - titleHeading), "```[^\n]*\n(.*?)```", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(b => b.Length > 0)
            .ToList();
        var descBlocks = Regex.Matches(markdown.Substring(descHeading), "```[^\n]*\n(.*?)```", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(b => b.Length > 0)
            .ToList();
        if (titleBlocks.Count == 0 || descBlocks.Count == 0)
        {
            Console.WriteLine("could not find the title and description blocks in " + args[0]);
            return 2;
        }

        string title = titleBlocks[0];
        string description = descBlocks[descBlocks.Count - 1];
        Console.WriteLine($"[localizer] title: {title}");
        Console.WriteLine($"[localizer] description: {description.Length} chars");

        if (!File.Exists(Path.Combine(Environment.CurrentDirectory, "steam_appid.txt")))
            Console.WriteLine("[localizer] warning: steam_appid.txt is not in " + Environment.CurrentDirectory);

        try
        {
            ESteamAPIInitResult initResult = SteamAPI.InitEx(out string initError);
            if (initResult != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                Console.WriteLine($"[localizer] SteamAPI.Init failed: {initResult} {initError}");
                return 1;
            }

            Console.WriteLine("[localizer] steam user: " + SteamFriends.GetPersonaName());

            var handle = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(ItemId));
            if (handle == UGCUpdateHandle_t.Invalid)
            {
                Console.WriteLine("[localizer] StartItemUpdate failed");
                return 1;
            }

            Console.WriteLine("[localizer] language set: " + SteamUGC.SetItemUpdateLanguage(handle, Language));
            Console.WriteLine("[localizer] title set:    " + SteamUGC.SetItemTitle(handle, title));
            Console.WriteLine("[localizer] desc set:     " + SteamUGC.SetItemDescription(handle, description));

            SteamAPICall_t call = SteamUGC.SubmitItemUpdate(handle, args[1]);
            if (call == SteamAPICall_t.Invalid)
            {
                Console.WriteLine("[localizer] SubmitItemUpdate refused the update");
                return 1;
            }

            EItemUpdateStatus last = (EItemUpdateStatus)(-1);
            bool started = false;
            for (int i = 0; i < 900; i++)
            {
                SteamAPI.RunCallbacks();
                EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress(handle, out ulong done, out ulong total);
                if (status != last)
                {
                    Console.WriteLine($"[localizer] status={status} {done}/{total}");
                    last = status;
                }
                if (status != EItemUpdateStatus.k_EItemUpdateStatusInvalid) started = true;
                else if (started) break;
                Thread.Sleep(200);
            }

            SteamAPI.RunCallbacks();
            Console.WriteLine("[localizer] done");
            return started ? 0 : 1;
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }
}
