# Validate DLC for Installation — stato e test da fare

Data: 2026-10-31. Tutto **non committato**. Memoria: `dlc-install-wizard.md`.

## Obiettivo
Installare un DLC senza che i suoi id/nomi/file/stringhe si scontrino con il gioco o con gli altri DLC della cartella Xenia (`...\4D530910\00000002`). Si modifica **solo il DLC da installare**, mai quelli già presenti.

Regola: EAGLE id 30 nel DLC1 e FORD id 30 nel DLC2 -> FORD passa al primo id libero con tutte le sue righe. Vale per ogni tabella (id auto, miniature, motori, ruote, colori...).

## Codice (cartella ForzaStudio)
- `DlcRelocate.cs` — parte database: toglie le righe identiche al gioco, cambia offerta, auto (`DbIds.RenumberCar` + MediaName `_2`, `_3`...), motori/trasmissioni con i figli `id*1000+n` (anche `List_TorqueCurve`), tutte le altre tabelle a chiave singola. Le tabelle "catalogo" (marche, famiglie, produttori) restano condivise se identiche.
- `DlcWorld.cs` — legge gioco + tutti i pacchetti installati sul posto (DB, nomi auto, offerte, file, livelli `Benchmark`).
- `DlcFixer.Install.cs` — (partial di `DlcFixer`) rinomina cartelle auto/ruote/`Tire[ABC]<auto>`, miniature, file dell'offerta, `zipmount.xml`; copia `<auto>_CMT.xml`; `MoveStrings` (trucco Benchmark: `_&(0x7CE9<<16 | chiave)`); `SaveTo` riscrive l'STFS.
- `DlcInstallForm.cs` — finestra, menu Tools > Validate DLC for Installation (riga aggiunta in `ArchiveEditorForm.cs`).
- `ForzaZip.Rename` — rinomina una voce senza ricomprimere.
- `ForzaStudioEVO.csproj` — 4 righe `Compile`.

## Test già fatto (da riga di comando)
- Pacchetto `...454D4E` = clone di `...454D4D` (offerta 300, stessi id e nomi).
- Risultato: `E:\DEV\fm4dev\DlcInstall\out\4E_new` (offerta 301, auto 8551-8557, nomi `_2`, motori 8558-8563, ruote 11210-11215, Benchmark.str 18 lingue).
- Backup dell'originale: `out\4E_original_backup`.
- `CheckMerge` ok, validatore "nessuna riga punta a righe mancanti", 398 file ok, seconda esecuzione = nessuna modifica.
- Harness: `E:\DEV\fm4dev\DlcInstall` (`inst.exe <pacchetto> <content> <FM4 DEV> <output> save`; compilare con `stub.cs` per `BackupPolicy`; `reloc.exe` solo DB).

## Da fare ai prossimi test
1. **In gioco:** chiudere Xenia, sostituire il 4E con `4E_new` (tenere il 4D). Verificare: due copie di ogni auto nel negozio, nomi e miniature giusti, 4D invariato, suono, ruote, colori, upgrade, guida.
2. **Finestra:** ricompilare (build in `bin\Debug` con Framework 4.0 MSBuild, app chiusa), aprire Tools > Validate DLC for Installation, Check + Install sul 4E originale. Mai provata a mano.
3. Controllare che il nome del pacchetto nello store sia quello atteso (stringhe in Benchmark; l'header STFS resta "FM3 Trial v.0.4").
4. Provare con un DLC vero di un'altra persona e con uno che porta altre string table (oggi gestite solo `Data_Car` e `ContentOffers`; il log segnala le altre).
5. Pacchetti in cartella (non STFS) come DLC da installare: non supportati da `SaveTo`.
6. Tabelle con chiave composta che si ripetono: solo segnalate, non spostate.
7. Collisione residua nota: due pacchetti che usano entrambi `Benchmark.str` (vince l'offerta più alta, che porta la copia cumulativa).
8. Snapshot JSON dei DLC originali (nome, lingue, stringhe): non fatto, si leggono i pacchetti installati a runtime. Da decidere se serve ancora.
9. Test su 10 DLC a caso + commit dopo l'ok in gioco.

## Prove da fare (aggiunte 2026-10-31, dopo il caso Genesis HKS)
Contesto: il pacchetto offer 104 (Genesis HKS) non si caricava in gioco perché i suoi `Combo_Colors.ID` (1473002...) collidevano con le righe delle auto 1643-1645 aggiunte dopo al DB di DEV. Sotto: cosa è cambiato (tutto non committato, compilato solo in cartella di scarto) e cosa va provato.

### Cosa è cambiato
- `DlcRelocate`: sposta anche le chiavi libere ma sotto il massimo di FM4 (`BelowGameLimit`), oltre a quelle usate dal gioco o da altri pacchetti. Dopo il Check il pulsante Install si attiva (prima restava spento con "errors remain").
- `DlcPackage.RelocateKeys`: la stessa operazione (`MoveRoots` + `MoveKeys`) alla creazione del pacchetto, sul DB di merge, confrontando con il gioco e i pacchetti installati (`DlcWorld.Load`). Vale anche per Add To Package (usa la cartella Xenia, non quella temporanea).
- `DbIds.PackDatabases`: ora estrae e mette in cache il merge dei pacchetti STFS (prima vedeva solo i `*_merge.slt` sciolti, quindi `Next`, `CarId`, `CarMake` e la duplicazione ignoravano i pacchetti reali).
- Massimi ID: un file per gioco in `docs/{FM2,FM3,FM4}/IdMax.json` e `docs/HZN/IdMax.{FH1,FH2}.json` (tutte le tabelle, chiave, massimo per colonna), generati da `tools/IdMax`, incorporati nell'exe e letti da `GameIdLimits.cs`. Sostituiscono `docs/GameIdLimits.json` (lasciato, non più usato).
- Regola ID, decisa dall'utente: nuovo ID = massimo di tutto + 1 (massimo ufficiale del gioco, gioco, pacchetti installati, pacchetto stesso). Non si riempiono i buchi. Esempio: pacchetto X con 2000-2003, un altro con 2002, 2003, 2007: X passa a 2002 -> 2008, 2003 -> 2009, il 2007 resta, il prossimo parte da 2010.

### Da provare
1. Compilare in `bin\Debug` (app chiusa) e ricontrollare se le altre modifiche in corso toccano gli stessi file.
2. Creare un pacchetto nuovo (una sola auto, per esempio Genesis HKS) e leggere nel report le chiavi spostate per tabella (motori, trasmissioni, ruote, colori, aerodinamica, curve di coppia, upgrade).
3. Copiare il pacchetto con un altro nome file nella cartella Xenia e passare la copia dal wizard (Check + Install): offerta nuova, auto `_2`, miniature, cartelle auto e ruote, stringhe in `Benchmark.str`.
4. Controlli da riga di comando sul risultato: nessuna chiave in comune con l'originale; tutte le chiavi sopra il massimo di FM4; nessun riferimento a righe mancanti (motore, trasmissione, ruote, colori); una seconda esecuzione non cambia nulla; l'originale identico byte per byte.
5. In gioco: due copie di ogni auto nel negozio, nomi e miniature giusti, acquisto e selezione in arcade, suono, ruote, colori, upgrade, guida.
6. Un pacchetto fatto prima della correzione (ID bassi di FM3), se se ne conserva uno: caso più vicino all'uso reale.
7. Add To Package: aggiungere un'auto a un pacchetto esistente e verificare che le chiavi nuove evitino quelle già nel pacchetto (le sue righe sono nel confronto).
8. Le chiavi spostate sono verificate solo sul DB: in gioco non è mai stato provato lo spostamento di motori, ruote e colori (vedi anche il punto 1 della sezione sopra).
9. `docs/*/IdMax* controllato su tutti i pacchetti presenti nella cartella Xenia (2026-10-31): FM2 10 pacchetti (8 con database), FM3 25 (17), FM4 20 (16 con database, gli altri sono i 4 pacchetti base), FH1 21 (16). I pacchetti senza `.slt` portano solo script `_execute.sql` (UPDATE/DELETE su righe esistenti, `INSERT ... SELECT` su auto esistenti, un solo `INSERT INTO CarExceptions` di FM2 per l auto 628) o solo file di auto (FH1: 5 pacchetti di una sola auto): nessun ID nuovo, quindi i massimi non cambiano. FH2: nessun pacchetto sul disco (non esiste la cartella 4D530AA4). Il ColoradoDirt di FH1 ha uno zip danneggiato nel file stesso (traccia, non database). Si rigenera con `tools/IdMax` se arrivano altri pacchetti.
