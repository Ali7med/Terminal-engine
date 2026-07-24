using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Terminal.Storage;

/// <summary>قالب أمر مُجمَّع بعدّاداته.</summary>
public sealed record CommandStat(
    string TemplateHash,
    string Template,
    string Sample,
    string? Shell,
    string? Cwd,
    int RunCount,
    int SuccessCount,
    int FailCount,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    bool IsPinned,
    bool IsBanned);

/// <summary>نمط خطأ ببصمته وحلّه المقبول إن وُجد.</summary>
public sealed record ErrorPattern(
    string Fingerprint,
    int? ExitCode,
    string Sample,
    string? Solution,
    int SeenCount,
    DateTimeOffset LastSeen);

/// <summary>قرار المستخدم في اقتراح.</summary>
public enum SuggestionVerdict
{
    /// <summary>عُرِض ولم يُبتّ فيه بعد.</summary>
    Pending = 0,

    /// <summary>قَبِله المستخدم.</summary>
    Accepted = 1,

    /// <summary>رفضه المستخدم (رفض دائم لهذا الموضوع).</summary>
    Rejected = -1,
}

/// <summary>
/// قاعدة المعرفة المحلّيّة لطبقة الـAI: ما تعلّمه التطبيق عن استعمال صاحبه.
/// <para><b>مبدآن حاكمان:</b> (1) <b>تجميع لا أرشفة</b> — قوالب بعدّادات لا سجلّ تنفيذات، فالنموّ
/// محدود بتنوّع الأوامر لا بعددها. (2) <b>الحجب قبل الكتابة</b> — كلّ نصّ يمرّ عبر مُنقّح الأسرار
/// المُمرَّر للبانِي قبل أن يلمس القرص، فلا يُخزَّن <c>--token=…</c> نصّاً صريحاً.</para>
/// <para>البيانات محلّيّة لهذا الجهاز فقط؛ لا مزامنة ولا إرسال. المخطّط يُنشأ كاملاً من أوّل موجة
/// كي لا تلزم هجرة لاحقة حين تُبنى الميزات التي تستهلكه.</para>
/// </summary>
public sealed class AiKnowledgeStore
{
    /// <summary>إصدار المخطّط — يُخزَّن في <c>ai_meta</c> لهجرات مستقبليّة.</summary>
    public const int SchemaVersion = 1;

    /// <summary>مدّة الاحتفاظ بالأحداث الخام قبل تقليمها.</summary>
    public static readonly TimeSpan EventRetention = TimeSpan.FromDays(90);

    /// <summary>سقف حجم ملفّ القاعدة قبل تقليم الأقدم.</summary>
    public const long MaxDatabaseBytes = 50L * 1024 * 1024;

    private readonly AppDatabase _db;
    private readonly Func<string, string> _redact;
    private readonly object _writeLock = new();

    /// <param name="db">مصنع الاتّصالات المشترك.</param>
    /// <param name="redact">
    /// مُنقّح الأسرار. إلزاميّ عمداً: جعل الحجب معامِلاً في البانِي يحوّل الضمانة من تعليق في
    /// التوثيق إلى شرط بنيويّ لا يمكن للمستدعي إغفاله.
    /// </param>
    public AiKnowledgeStore(AppDatabase db, Func<string, string> redact)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _redact = redact ?? throw new ArgumentNullException(nameof(redact));

        _db.Execute(
            """
            CREATE TABLE IF NOT EXISTS ai_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            -- قوالب الأوامر المُجمَّعة. المفتاح (قالب، صدفة): الصدفات قليلة فلا تُفجّر الصفوف،
            -- بينما المجلد يُحفظ كـ«آخر مجلد» فقط — مفتاح يشمله يعني صفّاً لكلّ مشروع لكلّ أمر.
            CREATE TABLE IF NOT EXISTS ai_command_stats (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                template_hash TEXT NOT NULL,
                template      TEXT NOT NULL,
                sample        TEXT NOT NULL,
                shell         TEXT NOT NULL DEFAULT '',
                cwd           TEXT NULL,
                run_count     INTEGER NOT NULL DEFAULT 0,
                success_count INTEGER NOT NULL DEFAULT 0,
                fail_count    INTEGER NOT NULL DEFAULT 0,
                first_seen    INTEGER NOT NULL,
                last_seen     INTEGER NOT NULL,
                is_pinned     INTEGER NOT NULL DEFAULT 0,
                is_banned     INTEGER NOT NULL DEFAULT 0,
                UNIQUE(template_hash, shell)
            );
            CREATE INDEX IF NOT EXISTS ix_ai_command_stats_rank
                ON ai_command_stats(run_count DESC, last_seen DESC);

            -- بصمات الأخطاء وحلولها المقبولة: أساس «رأيت هذا من قبل — الحل السابق» بلا أيّ نداء API.
            CREATE TABLE IF NOT EXISTS ai_error_patterns (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                fingerprint TEXT NOT NULL UNIQUE,
                exit_code   INTEGER NULL,
                sample      TEXT NOT NULL,
                solution    TEXT NULL,
                seen_count  INTEGER NOT NULL DEFAULT 1,
                first_seen  INTEGER NOT NULL,
                last_seen   INTEGER NOT NULL
            );

            -- حلقة التغذية الراجعة: ما اقتُرح وما قُبل ورُفض. الرفض دائم لموضوعه.
            CREATE TABLE IF NOT EXISTS ai_suggestions (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                kind         TEXT NOT NULL,
                subject_hash TEXT NOT NULL,
                payload      TEXT NOT NULL,
                verdict      INTEGER NOT NULL DEFAULT 0,
                created_at   INTEGER NOT NULL,
                decided_at   INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_ai_suggestions_subject
                ON ai_suggestions(kind, subject_hash);

            -- أحداث خام صغيرة (احتفاظ 90 يوماً) — لقياس الاتّجاهات لا لأرشفة السلوك.
            CREATE TABLE IF NOT EXISTS ai_events (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                kind       TEXT NOT NULL,
                detail     TEXT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_ai_events_created ON ai_events(created_at);

            -- قائمة «ليس سرّاً»: بصمات رموز أقرّ المستخدم أنّها ليست أسراراً، فلا تفرض معاينة
            -- قسريّة كلّ مرّة. نخزّن البصمة لا الرمز — القائمة نفسها يجب ألّا تصير مخزن أسرار.
            CREATE TABLE IF NOT EXISTS ai_redaction_allow (
                token_hash TEXT PRIMARY KEY,
                added_at   INTEGER NOT NULL
            );
            """);

        _db.Execute($"INSERT OR REPLACE INTO ai_meta (key, value) VALUES ('schema_version', '{SchemaVersion}');");
    }

    // ===== الالتقاط =====

    /// <summary>
    /// يسجّل تنفيذ أمر: يُنشئ القالب أو يزيد عدّاداته. <paramref name="succeeded"/> فارغ حين لا
    /// يُعرف رمز الخروج (بلا تكامل صدفة) — عندها يُحصى التشغيل بلا نجاح ولا فشل.
    /// </summary>
    public void RecordCommand(string command, string? shell = null, string? cwd = null, bool? succeeded = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        string safe = _redact(command);
        CommandTemplateInfo info = CommandTemplate.Normalize(safe);
        if (info.Template.Length == 0) return;

        long now = Now();
        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO ai_command_stats
                    (template_hash, template, sample, shell, cwd, run_count, success_count, fail_count, first_seen, last_seen)
                VALUES
                    ($hash, $template, $sample, $shell, $cwd, 1, $ok, $fail, $now, $now)
                ON CONFLICT(template_hash, shell) DO UPDATE SET
                    run_count     = run_count + 1,
                    success_count = success_count + $ok,
                    fail_count    = fail_count + $fail,
                    sample        = excluded.sample,
                    cwd           = COALESCE(excluded.cwd, ai_command_stats.cwd),
                    last_seen     = excluded.last_seen;
                """;
            cmd.Parameters.AddWithValue("$hash", info.Hash);
            cmd.Parameters.AddWithValue("$template", info.Template);
            cmd.Parameters.AddWithValue("$sample", Truncate(safe, 500));
            cmd.Parameters.AddWithValue("$shell", shell ?? string.Empty);
            cmd.Parameters.AddWithValue("$cwd", (object?)cwd ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ok", succeeded == true ? 1 : 0);
            cmd.Parameters.AddWithValue("$fail", succeeded == false ? 1 : 0);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>يسجّل ظهور خطأ ببصمته (يزيد العدّاد إن تكرّر). يعيد البصمة.</summary>
    public string RecordError(int? exitCode, string firstErrorLine)
    {
        string safe = _redact(firstErrorLine ?? string.Empty);
        string fingerprint = CommandTemplate.ErrorFingerprint(exitCode, safe);
        long now = Now();

        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO ai_error_patterns (fingerprint, exit_code, sample, seen_count, first_seen, last_seen)
                VALUES ($fp, $code, $sample, 1, $now, $now)
                ON CONFLICT(fingerprint) DO UPDATE SET
                    seen_count = seen_count + 1,
                    last_seen  = excluded.last_seen;
                """;
            cmd.Parameters.AddWithValue("$fp", fingerprint);
            cmd.Parameters.AddWithValue("$code", (object?)exitCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sample", Truncate(safe, 500));
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        return fingerprint;
    }

    /// <summary>يربط حلّاً مقبولاً ببصمة خطأ — هذا ما يُعرض لاحقاً بلا أيّ نداء API.</summary>
    public void SetErrorSolution(string fingerprint, string solution)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return;

        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE ai_error_patterns SET solution = $solution WHERE fingerprint = $fp;";
            cmd.Parameters.AddWithValue("$fp", fingerprint);
            cmd.Parameters.AddWithValue("$solution", Truncate(_redact(solution ?? string.Empty), 2000));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>يجد نمط خطأ ببصمته (للاستدعاء المحلّيّ «رأيت هذا من قبل»).</summary>
    public ErrorPattern? FindError(string fingerprint)
    {
        using SqliteConnection connection = _db.Connect();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT fingerprint, exit_code, sample, solution, seen_count, last_seen
            FROM ai_error_patterns WHERE fingerprint = $fp;
            """;
        cmd.Parameters.AddWithValue("$fp", fingerprint ?? string.Empty);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ErrorPattern(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)));
    }

    /// <summary>يسجّل حدثاً خاماً صغيراً (التقاط خفيف: تشغيل من الكتالوج، فعل دردشة، …).</summary>
    public void RecordEvent(string kind, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(kind)) return;

        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO ai_events (kind, detail, created_at) VALUES ($kind, $detail, $now);";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$detail",
                detail is null ? DBNull.Value : Truncate(_redact(detail), 500));
            cmd.Parameters.AddWithValue("$now", Now());
            cmd.ExecuteNonQuery();
        }
    }

    // ===== الاقتراحات =====

    /// <summary>يسجّل اقتراحاً مَعروضاً. يعيد معرّفه لتحديث قراره لاحقاً.</summary>
    public long RecordSuggestion(string kind, string subjectHash, string payload)
    {
        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO ai_suggestions (kind, subject_hash, payload, verdict, created_at)
                VALUES ($kind, $subject, $payload, 0, $now);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$kind", kind ?? "");
            cmd.Parameters.AddWithValue("$subject", subjectHash ?? "");
            cmd.Parameters.AddWithValue("$payload", Truncate(_redact(payload ?? ""), 1000));
            cmd.Parameters.AddWithValue("$now", Now());
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }

    /// <summary>يثبّت قرار المستخدم في اقتراح.</summary>
    public void DecideSuggestion(long id, SuggestionVerdict verdict)
    {
        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE ai_suggestions SET verdict = $verdict, decided_at = $now WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$verdict", (int)verdict);
            cmd.Parameters.AddWithValue("$now", Now());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// هل رُفض اقتراح بهذا النوع والموضوع من قبل؟ الرفض دائم — إعادة عرض اقتراح مرفوض هي أسرع
    /// طريق لإفقاد المستخدم ثقته بالاقتراحات كلّها.
    /// </summary>
    public bool WasRejected(string kind, string subjectHash)
    {
        using SqliteConnection connection = _db.Connect();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT 1 FROM ai_suggestions
            WHERE kind = $kind AND subject_hash = $subject AND verdict = -1
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$kind", kind ?? "");
        cmd.Parameters.AddWithValue("$subject", subjectHash ?? "");
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>نسبة قبول الاقتراحات المبتوت فيها (0..1)، أو null إن لم يُبتّ في شيء بعد.</summary>
    public double? AcceptanceRate()
    {
        using SqliteConnection connection = _db.Connect();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT
                SUM(CASE WHEN verdict = 1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN verdict <> 0 THEN 1 ELSE 0 END)
            FROM ai_suggestions;
            """;
        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(1)) return null;
        int decided = reader.GetInt32(1);
        return decided == 0 ? null : reader.GetInt32(0) / (double)decided;
    }

    // ===== القراءة والترتيب =====

    /// <summary>
    /// أعلى القوالب ترتيباً محلّيّاً: <c>وزن×تكرار + اضمحلال حداثة</c>. دالّة حتميّة صرفة تعمل
    /// بلا أيّ مزوّد ولا اتّصال — وهذا ما يجعل قيمة التعلّم قائمة حتى بصفر مفاتيح.
    /// </summary>
    public IReadOnlyList<CommandStat> TopCommands(int limit = 20, string? shell = null)
    {
        var results = new List<CommandStat>();
        if (limit <= 0) return results;

        using SqliteConnection connection = _db.Connect();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT template_hash, template, sample, shell, cwd, run_count, success_count, fail_count,
                   first_seen, last_seen, is_pinned, is_banned
            FROM ai_command_stats
            WHERE is_banned = 0
              AND ($shell IS NULL OR shell = $shell)
            ORDER BY
                is_pinned DESC,
                (run_count * 1.0) / (1.0 + (($now - last_seen) / 86400000.0)) DESC,
                last_seen DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$shell", (object?)shell ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read()) results.Add(ReadStat(reader));
        return results;
    }

    /// <summary>
    /// القوالب المرشّحة لاقتراح «احفظه في الكتالوج»: تجاوزت العتبة، وليست محظورة، ولم تُرفَض من قبل.
    /// المطابقة مع الكتالوج نفسه مسؤوليّة المستدعي (بنفس المُطبِّع، مقارنة بصمات).
    /// </summary>
    public IReadOnlyList<CommandStat> CatalogCandidates(int minRuns = 5, int withinDays = 30, int limit = 5)
    {
        var results = new List<CommandStat>();
        long since = DateTimeOffset.UtcNow.AddDays(-withinDays).ToUnixTimeMilliseconds();

        using SqliteConnection connection = _db.Connect();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT s.template_hash, s.template, s.sample, s.shell, s.cwd, s.run_count, s.success_count,
                   s.fail_count, s.first_seen, s.last_seen, s.is_pinned, s.is_banned
            FROM ai_command_stats s
            WHERE s.is_banned = 0
              AND s.run_count >= $minRuns
              AND s.last_seen >= $since
              AND NOT EXISTS (
                    SELECT 1 FROM ai_suggestions g
                    WHERE g.kind = 'catalog' AND g.subject_hash = s.template_hash)
            ORDER BY s.run_count DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$minRuns", minRuns);
        cmd.Parameters.AddWithValue("$since", since);
        cmd.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read()) results.Add(ReadStat(reader));
        return results;
    }

    private static CommandStat ReadStat(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) || r.GetString(3).Length == 0 ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.GetInt32(5), r.GetInt32(6), r.GetInt32(7),
        DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(8)),
        DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(9)),
        r.GetInt32(10) != 0, r.GetInt32(11) != 0);

    /// <summary>أحدث أنماط الأخطاء (الأكثر تكراراً أوّلاً) — لعرضها في «ذاكرة التطبيق».</summary>
    public IReadOnlyList<ErrorPattern> RecentErrors(int limit = 100)
    {
        var results = new List<ErrorPattern>();
        if (limit <= 0) return results;

        using SqliteConnection connection = _db.Connect();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT fingerprint, exit_code, sample, solution, seen_count, last_seen
            FROM ai_error_patterns
            ORDER BY (solution IS NOT NULL) DESC, seen_count DESC, last_seen DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ErrorPattern(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5))));
        }
        return results;
    }

    /// <summary>سجلّ الاقتراحات (الأحدث أوّلاً) بنوعها وقرارها — شفافيّة حلقة التغذية الراجعة.</summary>
    public IReadOnlyList<(string Kind, string Payload, SuggestionVerdict Verdict)> RecentSuggestions(int limit = 100)
    {
        var results = new List<(string, string, SuggestionVerdict)>();
        if (limit <= 0) return results;

        using SqliteConnection connection = _db.Connect();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT kind, payload, verdict FROM ai_suggestions ORDER BY id DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1), (SuggestionVerdict)reader.GetInt32(2)));

        return results;
    }

    // ===== تحكّم المستخدم (نافذة «ذاكرة التطبيق») =====

    /// <summary>يثبّت قالباً أو يفكّ تثبيته (يصعد أعلى الترتيب).</summary>
    public void SetPinned(string templateHash, bool pinned) => SetFlag(templateHash, "is_pinned", pinned);

    /// <summary>يحظر قالباً فلا يظهر في الترتيب ولا الاقتراحات.</summary>
    public void SetBanned(string templateHash, bool banned) => SetFlag(templateHash, "is_banned", banned);

    private void SetFlag(string templateHash, string column, bool value)
    {
        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"UPDATE ai_command_stats SET {column} = $v WHERE template_hash = $hash;";
            cmd.Parameters.AddWithValue("$hash", templateHash ?? "");
            cmd.Parameters.AddWithValue("$v", value ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// حذف متسلسل لقالب: القالب ثمّ اقتراحاته. الحذف الجزئيّ يترك اقتراحاً «يتيماً» يعود للظهور
    /// عن موضوع حذفه المستخدم — وهو أسوأ من عدم الحذف.
    /// </summary>
    public void DeleteCommand(string templateHash)
    {
        if (string.IsNullOrEmpty(templateHash)) return;

        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteTransaction tx = connection.BeginTransaction();

            using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    DELETE FROM ai_suggestions WHERE subject_hash = $hash;
                    DELETE FROM ai_command_stats WHERE template_hash = $hash;
                    """;
                cmd.Parameters.AddWithValue("$hash", templateHash);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    /// <summary>يمسح كلّ ما تعلّمه التطبيق (زرّ «امسح كل شيء»).</summary>
    public void ClearAll()
    {
        lock (_writeLock)
        {
            _db.Execute(
                """
                DELETE FROM ai_command_stats;
                DELETE FROM ai_error_patterns;
                DELETE FROM ai_suggestions;
                DELETE FROM ai_events;
                DELETE FROM ai_redaction_allow;
                """);
            _db.Execute("VACUUM;");
        }
    }

    // ===== قائمة «ليس سرّاً» =====

    /// <summary>يضيف بصمة رمز أقرّ المستخدم أنّه ليس سرّاً (نخزّن البصمة لا الرمز).</summary>
    public void AllowToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        lock (_writeLock)
        {
            using SqliteConnection connection = _db.Connect();
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT OR IGNORE INTO ai_redaction_allow (token_hash, added_at) VALUES ($hash, $now);";
            cmd.Parameters.AddWithValue("$hash", CommandTemplate.Fingerprint(token));
            cmd.Parameters.AddWithValue("$now", Now());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>كلّ بصمات «ليس سرّاً» — يحمّلها المُنقّح مرّة عند الإقلاع.</summary>
    public IReadOnlyCollection<string> AllowedTokenHashes()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using SqliteConnection connection = _db.Connect();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT token_hash FROM ai_redaction_allow;";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read()) set.Add(reader.GetString(0));
        return set;
    }

    // ===== الصيانة =====

    /// <summary>
    /// تقليم دوريّ: يحذف الأحداث الأقدم من مدّة الاحتفاظ، ثمّ — إن تجاوز الملفّ السقف — يحذف
    /// أقدم الأحداث فأقلّ القوالب استعمالاً (بلا مساس بالمثبَّت)، ثمّ يضغط الملفّ.
    /// يعمل على خيط خلفيّ؛ آمن للاستدعاء المتكرّر.
    /// </summary>
    public void Maintain()
    {
        long cutoff = DateTimeOffset.UtcNow.Subtract(EventRetention).ToUnixTimeMilliseconds();

        lock (_writeLock)
        {
            using (SqliteConnection connection = _db.Connect())
            using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM ai_events WHERE created_at < $cutoff;";
                cmd.Parameters.AddWithValue("$cutoff", cutoff);
                cmd.ExecuteNonQuery();
            }

            if (FileSize() <= MaxDatabaseBytes)
                return;

            using (SqliteConnection connection = _db.Connect())
            using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText =
                    """
                    DELETE FROM ai_events
                    WHERE id IN (SELECT id FROM ai_events ORDER BY created_at ASC LIMIT 5000);

                    DELETE FROM ai_command_stats
                    WHERE is_pinned = 0
                      AND id IN (SELECT id FROM ai_command_stats
                                 WHERE is_pinned = 0
                                 ORDER BY run_count ASC, last_seen ASC
                                 LIMIT 500);
                    """;
                cmd.ExecuteNonQuery();
            }

            _db.Execute("VACUUM;");
        }
    }

    /// <summary>حجم ملفّ القاعدة بالبايت (0 إن تعذّرت قراءته).</summary>
    public long FileSize()
    {
        try
        {
            var info = new FileInfo(_db.FilePath);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max];
}
