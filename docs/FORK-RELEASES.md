# Fork: Releases und eigenes Plugin-Repository

Dieser Fork baut Releases per Git-Tag und liefert sie über ein eigenes Jellyfin-Plugin-Repository aus.
Alles dafür liegt in **neuen Dateien**, die Upstream nicht kennt, damit `git merge upstream/main`
konflikt-frei bleibt:

- `.github/workflows/publish-fork-manifest.yml`
- `scripts/update-fork-manifest.py`
- diese Datei

Den vorhandenen Workflow `build-release.yml` nutzt der Fork unverändert (Tag `vX.Y.Z.W` → Build →
GitHub Release mit `jellyfin-xtream-library_X.Y.Z.W.zip`).

## Einmalige Einrichtung

1. **Actions-Rechte:** Repo → Settings → Actions → General → Workflow permissions →
   *Read and write permissions*.
2. **Erstes Release erzeugen** (siehe unten). Das legt den Branch `gh-pages` mit `manifest.json` an.
3. **GitHub Pages aktivieren:** Settings → Pages → Source *Deploy from a branch* →
   Branch `gh-pages`, Ordner `/ (root)`.
4. **In Jellyfin eintragen:** Dashboard → Plugins → Repositories → Hinzufügen:

   ```
   https://moritzgrossmann.github.io/Jellyfin-Xtream-Library/manifest.json
   ```

   Ohne Pages funktioniert auch
   `https://raw.githubusercontent.com/MoritzGrossmann/Jellyfin-Xtream-Library/gh-pages/manifest.json`.

Das Plugin behält die GUID von Upstream. Eine bereits installierte Upstream-Version wird dadurch
aktualisiert, nicht doppelt installiert. Dann nur eines der beiden Repositories in Jellyfin eingetragen lassen.

## Ein Release erstellen

```bash
# Version in Jellyfin.Xtream.Library/Jellyfin.Xtream.Library.csproj anheben
# (AssemblyVersion und FileVersion), dann:
git add -A
git commit -m "Release v2.0.28.1: Beschreibung"
git tag -a v2.0.28.1 -m "Was sich geändert hat (wird zum Changelog im Manifest)"
git push origin main --tags
```

Der Ablauf danach läuft allein: *Build and Release* baut und lädt das ZIP hoch, danach trägt
*Publish Fork Manifest* Version, Checksumme und Changelog in `gh-pages/manifest.json` ein.
Changelog-Quelle ist der Text des Release (falls gepflegt), sonst die Nachricht des annotierten Tags.

Ein Manifest-Eintrag lässt sich jederzeit neu schreiben: Actions → *Publish Fork Manifest* →
Run workflow → Tag angeben.

`targetAbi` wird aus der `Jellyfin.Controller`-Version in der csproj abgeleitet (12.0.0 → `12.0.0.0`).

## Versionsschema: keine Kollision mit Upstream

Upstream zählt die ersten drei Stellen hoch und lässt die vierte auf `0`. Der Fork nutzt deshalb die
**vierte Stelle** für eigene Releases:

| Version | Bedeutung |
|---|---|
| `2.0.28.0` | Upstream-Stand |
| `2.0.28.1` | Fork-Release auf Basis von 2.0.28 |
| `2.0.29.0` | Upstream-Stand, nach dem Merge |
| `2.0.29.1` | Fork-Release auf Basis von 2.0.29 |

## Änderungen von Upstream einspielen

Einmalig:

```bash
git remote add upstream https://github.com/firestaerter3/Jellyfin-Xtream-Library.git
```

Danach bei Bedarf:

```bash
git fetch upstream --no-tags
git merge upstream/main
```

- **`--no-tags` ist wichtig.** Würdest du Upstream-Tags in den Fork pushen, baut `build-release.yml`
  für jeden davon ein Release im Fork.
- Zu erwarten ist höchstens ein Konflikt in der csproj bei `AssemblyVersion`/`FileVersion`.
  Nimm die Upstream-Zahl und setze sie danach für dein Release auf die nächste `…​.1`.
- Eigene Änderungen an Upstream-Dateien erzeugen Konflikte. Halte sie klein und möglichst
  in eigenen Dateien, dann bleibt das Mergen einfach.
- Ein Rebase auf `upstream/main` statt Merge ist möglich, schreibt aber deine Historie um und
  verschiebt dabei Tags. Merge ist hier die sichere Wahl.
