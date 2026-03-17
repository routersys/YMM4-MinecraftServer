using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;

namespace MinecraftHost.ViewModels.Items;

public sealed class PlayerRosterCoordinator
{
    private static readonly Regex JoinPattern = new(@"INFO\]:\s+([a-zA-Z0-9_]{2,16})\s+joined the game", RegexOptions.Compiled);
    private static readonly Regex LeavePattern = new(@"INFO\]:\s+([a-zA-Z0-9_]{2,16})\s+left the game", RegexOptions.Compiled);
    private readonly ObservableCollection<PlayerViewModel> _players;

    public PlayerRosterCoordinator(ObservableCollection<PlayerViewModel> players)
    {
        _players = players;
    }

    public void ProcessLine(string line)
    {
        var joinMatch = JoinPattern.Match(line);
        if (joinMatch.Success)
        {
            var player = joinMatch.Groups[1].Value;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_players.Any(p => p.Name == player))
                    _players.Add(new PlayerViewModel(player));
            }));
            return;
        }

        var leaveMatch = LeavePattern.Match(line);
        if (!leaveMatch.Success)
            return;

        var leavingPlayer = leaveMatch.Groups[1].Value;
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var target = _players.FirstOrDefault(p => p.Name == leavingPlayer);
            if (target is not null)
                _players.Remove(target);
        }));
    }
}