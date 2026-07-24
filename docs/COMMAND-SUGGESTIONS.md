# كتالوج الأوامر والاقتراحات الذكيّة

مرجع لِما يقترحه صندوق التأليف. **مصدر الحقيقة هو الكود**:
[`Services/CommandCatalog.cs`](../Services/CommandCatalog.cs) — هذا الملفّ عرضٌ قرائيّ له.
منطق الاختيار في `BuildSuggestions` داخل [`Controls/TerminalTabView.xaml.cs`](../Controls/TerminalTabView.xaml.cs).

## كيف تُختار الاقتراحات

| الموضع في السطر | ما يُقترَح |
| --- | --- |
| الكلمة الأولى | أوامر الكتالوج (حسب عائلة الصدفة) + تاريخ الجلسة + السجلّ العامّ |
| بعد أمر مركَّب بلا فرعيّ (`git `) | أوامره الفرعيّة |
| وسيط يبدأ بـ `-` أو `/` | خيارات الأمر الشائعة |
| وسيط عاديّ | **حسب نوع وسيط الأمر** (الجدول أدناه) |

أنواع الوسائط (`ArgKind`):

| النوع | ما يظهر | الوسم |
| --- | --- | --- |
| `Directory` | مجلدات فقط + `..` | مجلد |
| `File` | ملفّات ومجلدات (المجلد للعبور) | ملفّ / مجلد |
| `Any` | ملفّات ومجلدات | ملفّ / مجلد |
| `GitRef` | فروع المستودع (`refs/heads` + `packed-refs`) ثمّ الملفّات | فرع |
| `NpmScript` | سكربتات أقرب `package.json` + نصّ الأمر كوصف | سكربت |
| `Command` | أسماء أوامر الكتالوج | أمر |
| `Process` | أسماء العمليّات الجارية | عمليّة |
| `None` | لا وسائط — السجلّ والخيارات فقط | سجلّ |

**تفاصيل الإكمال:** المسارات تُبنى من **مجلد العمل الحيّ** (يُستخرَج من الموجّه فيتبع `cd` فعليّاً)، وتدعم
المسار الجزئيّ (`src/comp`) والمطلق و`~`؛ المجلدات قبل الملفّات؛ المخفيّة لا تظهر حتّى تُكتب النقطة؛ الاسم
الذي يحوي مسافة يُقتبَس تلقائيّاً. الأغلفة `sudo` · `time` · `watch` · `xargs` · `env` · `nohup` · `doas`
تُتخطّى لاستنتاج الأمر الحقيقيّ بعدها.

---

## أوامر يونكس (bash · zsh · git-bash · wsl)

| الأمر | الوسيط المقترَح | خيارات مقترَحة |
| --- | --- | --- |
| `cd` | **مجلدات** | `-` · `..` |
| `ls` | مجلدات | `-l` `-la` `-lh` `-a` `-R` `--color` |
| `pwd` · `popd` · `df` · `env` · `echo` · `history` · `clear` · `exit` | — | `df -h` |
| `pushd` | مجلدات | |
| `mkdir` · `rmdir` | مجلدات | `-p` |
| `rm` | ملفّات ومجلدات | `-r` `-f` `-rf` `-i` `-v` |
| `cp` | ملفّات ومجلدات | `-r` `-a` `-v` `-u` |
| `mv` | ملفّات ومجلدات | `-v` `-i` `-n` |
| `touch` · `nano` · `vim` · `vi` · `nvim` | **ملفّات** | |
| `cat` | ملفّات | `-n` |
| `less` · `more` · `sed` · `awk` · `sort` · `uniq` | ملفّات | |
| `head` · `tail` | ملفّات | `-n` · `-f` |
| `wc` | ملفّات | `-l` `-w` `-c` |
| `diff` | ملفّات | `-u` `-r` |
| `grep` | ملفّات ومجلدات | `-r` `-i` `-n` `-v` `-E` `-l` `-w` |
| `rg` | ملفّات ومجلدات | `-i` `-n` `-l` `-g` `--type` |
| `find` | مجلدات | `-name` `-type f` `-type d` `-maxdepth` |
| `chmod` | ملفّات ومجلدات | `+x` `-R` `755` `644` |
| `chown` | ملفّات ومجلدات | `-R` |
| `ln` | ملفّات ومجلدات | `-s` |
| `du` | مجلدات | `-sh` `-h` |
| `tar` | ملفّات ومجلدات | `-xzf` `-czf` `-tf` |
| `zip` | ملفّات ومجلدات | `-r` |
| `unzip` · `source` | ملفّات | |
| `which` · `man` · `time` · `sudo` · `xargs` | **اسم أمر** | |
| `watch` | اسم أمر | `-n` |
| `ps` | — | `aux` `-ef` |
| `kill` | **العمليّات الجارية** | `-9` |
| `top` · `htop` | — | |
| `curl` | — | `-s` `-L` `-O` `-X POST` `-H` `-d` |
| `wget` | — | `-O` `-c` |
| `ssh` | — | `-p` `-i` |
| `scp` | ملفّات ومجلدات | `-r` `-P` |
| `rsync` | ملفّات ومجلدات | `-avz` `--delete` |
| `open` | ملفّات ومجلدات | |
| `code` | ملفّات ومجلدات | `.` `-r` `-n` |

## أوامر PowerShell

| الأمر | الوسيط المقترَح | خيارات مقترَحة |
| --- | --- | --- |
| `cd` · `Set-Location` · `ls` · `dir` | **مجلدات** | |
| `Get-ChildItem` | مجلدات | `-Recurse` `-Force` `-Filter` `-File` `-Directory` |
| `Get-Content` | **ملفّات** | `-Tail` `-TotalCount` `-Wait` `-Raw` |
| `Set-Content` | ملفّات | `-Encoding utf8` |
| `Add-Content` | ملفّات | |
| `New-Item` | ملفّات ومجلدات | `-ItemType Directory` `-ItemType File` `-Force` |
| `Remove-Item` | ملفّات ومجلدات | `-Recurse` `-Force` `-Confirm:$false` |
| `Copy-Item` | ملفّات ومجلدات | `-Recurse` `-Force` |
| `Move-Item` | ملفّات ومجلدات | `-Force` |
| `Rename-Item` · `Test-Path` · `Start-Process` | ملفّات ومجلدات | |
| `Select-String` | ملفّات ومجلدات | `-Pattern` `-Path` `-CaseSensitive` |
| `Get-Process` | **العمليّات الجارية** | |
| `Stop-Process` | العمليّات الجارية | `-Name` `-Id` `-Force` |
| `Expand-Archive` | ملفّات | `-DestinationPath` `-Force` |
| `Compress-Archive` | ملفّات ومجلدات | `-DestinationPath` `-Force` |
| `Get-Command` | **اسم أمر** | |
| `Get-Help` | اسم أمر | `-Examples` `-Full` |
| `Select-Object` | — | `-First` `-Last` `-Property` |
| `Measure-Object` | — | `-Line` `-Word` `-Sum` |
| `Where-Object` · `ForEach-Object` · `ConvertFrom-Json` · `Import-Module` · `Get-Service` · `pwd` · `cls` · `Clear-Host` · `whoami` · `exit` | — | |
| `Invoke-WebRequest` | — | `-Uri` `-Method` `-OutFile` |
| `code` | ملفّات ومجلدات | `.` `-r` `-n` |

## أوامر cmd.exe

| الأمر | الوسيط المقترَح | خيارات مقترَحة |
| --- | --- | --- |
| `cd` | **مجلدات** | `/d` `..` |
| `dir` | مجلدات | `/b` `/s` `/a` |
| `md` · `rd` | مجلدات | `rd /s /q` |
| `type` | **ملفّات** | |
| `del` | ملفّات | `/f` `/q` `/s` |
| `copy` · `move` · `start` | ملفّات ومجلدات | |
| `xcopy` | ملفّات ومجلدات | `/E` `/I` `/Y` |
| `where` | **اسم أمر** | |
| `taskkill` | **العمليّات الجارية** | `/F` `/IM` `/PID` |
| `tasklist` · `echo` · `set` · `cls` · `exit` | — | |
| `ipconfig` | — | `/all` `/flushdns` |
| `ping` | — | `-t` `-n` |

## أدوات التطوير (في كلّ الصدفات)

### `git`
**فرعيّات:** `status` `add` `commit` `push` `pull` `fetch` `clone` `checkout` `switch` `branch` `merge`
`rebase` `log` `diff` `stash` `reset` `restore` `remote` `tag` `init` `show` `cherry-pick` `worktree`
`blame` `revert` `clean`

| الفرعيّ | الوسيط المقترَح |
| --- | --- |
| `checkout` · `switch` · `merge` · `rebase` · `branch` · `cherry-pick` · `revert` · `show` · `tag` | **فروع المستودع** |
| `add` · `diff` · `restore` | ملفّات ومجلدات |
| `init` | مجلدات |
| `status` · `push` · `pull` · `fetch` · `stash` · `log` · `commit` · `clone` | — |

### `npm` · `pnpm` · `yarn` · `bun`
**فرعيّات npm:** `install` `i` `run` `start` `test` `build` `ci` `uninstall` `update` `init` `publish`
`audit` `outdated` `exec` `link` `list`
**pnpm:** `install` `add` `run` `dev` `build` `test` `remove` `update` `dlx` `exec` `list` ·
**yarn:** `install` `add` `run` `dev` `build` `test` `remove` `upgrade` `dlx` ·
**bun:** `install` `add` `run` `dev` `build` `test` `remove` `x`

| الفرعيّ | الوسيط المقترَح |
| --- | --- |
| `run` | **سكربتات `package.json`** (مع نصّ السكربت كوصف) |

### `dotnet`
**فرعيّات:** `build` `run` `test` `restore` `publish` `add` `new` `clean` `watch` `format` `sln` `nuget`
`tool` `list` `pack` — **خيارات:** `--version` `-c Release` `--no-build`
`build`/`test`/`restore`/`clean` ⇒ ملفّات ومجلدات (المشروع/الحلّ).

### `docker` · `docker-compose`
**فرعيّات docker:** `ps` `images` `run` `exec` `build` `pull` `push` `logs` `stop` `start` `restart`
`rm` `rmi` `compose` `volume` `network` `inspect` `system` — `build` ⇒ **مجلدات** (سياق البناء).
**compose:** `up` `down` `build` `logs` `ps` `restart` `exec` `pull`

### بقيّة الأدوات

| الأداة | الفرعيّات / الوسيط |
| --- | --- |
| `node` | **ملفّات** · `-v` `--watch` |
| `npx` | — |
| `python` · `python3` · `py` | **ملفّات** · `-m` `-V` (`py -3`) |
| `pip` | `install` `uninstall` `list` `freeze` `show` `download` |
| `cargo` | `build` `run` `test` `check` `new` `add` `fmt` `clippy` `update` `install` |
| `go` | `run` (ملفّات) · `build` (ملفّات ومجلدات) · `test` `mod` `get` `fmt` `vet` `install` |
| `make` | — |
| `gh` | `pr` `issue` `repo` `run` `release` `auth` `browse` `workflow` |

---

## إضافة أمر إلى الكتالوج

في [`Services/CommandCatalog.cs`](../Services/CommandCatalog.cs) أضف سطراً إلى `UnixSpecs` أو `PwshSpecs`
أو `CmdSpecs` أو `DevSpecs` (الأخيرة تظهر في كلّ الصدفات):

```csharp
new("mycmd", B("وصف عربيّ", "English desc"), ArgKind.Directory,
    Subs:  new[]{ "up", "down" },
    Flags: new[]{ "--force" },
    SubArg: new(StringComparer.OrdinalIgnoreCase) { ["up"] = ArgKind.File }),
```

لا حاجة لأيّ تعديل آخر — القائمة تُبنى من الكتالوج مباشرةً.
