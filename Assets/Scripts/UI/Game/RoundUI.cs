using TMPro;
using UnityEngine;
using Gameplay.Leaderboard;
using Unity.MP_FPS;

public class RoundUI : MonoBehaviour
{
    [SerializeField] private TMP_Text roundText;
    public TMP_Text roundWinner;

    private void Update()
    {
        //hide round ui until in game
        if (GameSettings.Instance == null || GameSettings.Instance.GameState != GlobalGameState.InGame)
        {
            if (roundText != null)
                roundText.text = string.Empty;

            if (roundWinner != null)
                roundWinner.text = string.Empty;

            return;
        }
        if (LeaderboardManager.Instance == null)
            return;

        var leaderboard = LeaderboardManager.Instance;

        switch (leaderboard.CurrentPhase)
        {
            case LeaderboardManager.RoundPhase.Fighting:
                roundText.text = $"ROUND {leaderboard.CurrentRound}";
                roundWinner.text = string.Empty;
                break;

            case LeaderboardManager.RoundPhase.BuildMode:
                roundText.text =
                    $"BUILD MODE\n" +
                    $"NEXT ROUND IN: {Mathf.CeilToInt(leaderboard.BuildTimer)}";

                roundWinner.text = leaderboard.LastWinningTeamId switch
                {
                    0 => "BLUE TEAM WON",
                    1 => "GREEN TEAM WON",
                    _ => string.Empty
                };

                break;

            case LeaderboardManager.RoundPhase.MatchOver:
                roundText.text = "MATCH OVER";
                roundWinner.text = leaderboard.LastWinningTeamId switch
                {
                    0 => "BLUE TEAM WON",
                    1 => "GREEN TEAM WON",
                    _ => string.Empty
                };
                break;
        }


    }
}