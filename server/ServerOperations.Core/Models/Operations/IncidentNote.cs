namespace ServerOperations.Core.Models.Operations;

/// <summary>
/// インシデントへの対応メモ。人が書いた記録を残す。
///
/// IncidentLogは収集したログを入れる箱で、書き手は機械に限られる。
/// 「何が原因だったか」「次に何を見るか」を残す場所が別に要る。
/// 同じ障害が再発したとき、前回の対応を引けるようにするのが目的。
/// </summary>
public class IncidentNote
{
    public long Id { get; set; }

    public long IncidentId { get; set; }

    public Incident? Incident { get; set; }

    /// <summary>書いた人。利用者が消されても記録は残すためnull許容にする。</summary>
    public long? AuthorUserId { get; set; }

    /// <summary>書いた時点の利用者名。後から利用者を辿れなくなっても誰の記録か分かるようにする。</summary>
    public required string AuthorName { get; set; }

    public required string Body { get; set; }

    public DateTime CreatedAt { get; set; }
}
