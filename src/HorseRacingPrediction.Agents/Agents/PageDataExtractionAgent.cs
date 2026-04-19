using System.Text;
using HorseRacingPrediction.Agents.Browser;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// Web ページの生テキストを受け取り、LLM を使って
/// ヘッダー・フッター・ナビゲーション等のノイズを除去した
/// 整理済みテキスト＋リンク集を出力するページ整形エージェント。
/// <para>
/// PlaywrightTools のツールが返すページ本文を、
/// WebBrowserAgent が判断しやすいクリーンなドキュメントに変換する。
/// 要約しすぎず、本文の情報をできるだけ保持する。
/// </para>
/// </summary>
public sealed class PageDataExtractionAgent
{
    private const string SystemPrompt = """
        あなたはWebページの生テキストを読みやすく整形する専門エージェントです。
        ブラウザから取得したページの生テキストを受け取り、整理されたドキュメントとして出力します。

        ## 整形ルール
        - ヘッダー、フッター、サイドバー、ナビゲーションメニュー、Cookie通知、広告、ログインボタン等のUI要素テキストは省く
        - 本文コンテンツ（記事、データ表、リスト、説明文等）はそのまま保持する。できる限り元の記述のままで要約しない
        - 表形式のデータ（レース結果、出馬表等）はMarkdownの表として整形する
        - 箇条書きやリストはそのまま維持する
        - ページ内で見つかったコンテンツに関連するリンクがあれば、末尾に「## リンク」セクションとしてまとめる
        - リンクは `- [タイトル](URL)` の Markdown 形式で出力する
        - 検索エンジン、メール、ログイン系のリンクは除外する

        ## 検索結果ページの場合
        URL が bing.com や google.com の検索結果ページの場合は、以下のルールを適用する:
        - 各検索結果の**タイトル**と**説明文**を箇条書きで簡潔にまとめる
        - 検索結果以外のUI要素（関連検索、広告、「他の人はこちらも検索」等）はすべて省く
        - 出力形式: `- **タイトル**: 説明文` のリストにする

        ## 出力形式
        Markdown形式で出力してください。余計な前置きや説明は不要です。
        """;

    private const int MaxInputLength = 15_000;

    private readonly IChatClient _chatClient;

    public PageDataExtractionAgent(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    /// <summary>
    /// ページの生テキストを LLM で整形し、クリーンなドキュメントを返す。
    /// </summary>
    /// <param name="rawPageText">ブラウザから取得したページの innerText</param>
    /// <param name="pageUrl">ページの URL（コンテキスト情報として使用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>整形済みの Markdown テキスト</returns>
    public async Task<string> FormatPageContentAsync(
        string rawPageText,
        string pageUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPageText))
        {
            return string.Empty;
        }

        var truncatedText = rawPageText.Length > MaxInputLength
            ? rawPageText[..MaxInputLength]
            : rawPageText;

        var userMessage = $"""
            以下のWebページの内容を整形してください。

            URL: {pageUrl}

            --- ページ生テキスト ---
            {truncatedText}
            """;

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userMessage),
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(response.Text)
                ? rawPageText
                : response.Text;
        }
        catch
        {
            // LLM 呼び出しに失敗した場合は生テキストをそのまま返す
            return rawPageText;
        }
    }
}
