# Stringtable "Benchmark" per i pacchetti DLC (analisi 2026-10-31, DA RIPRENDERE)

Stato: solo analisi, nessun codice cambiato. Nota anche in memoria: `benchmark-string-table-trick`.

## Origine
Port di Mount Panorama (Bathurst) v0.6.1 di un collega: `E:\DEV\ported_track\71DF7025...4D` (STFS).
Spiegazione di Aiden-Piercee: "scrivi in una tabella diversa e punta indietro, il problema sparisce".
Estratto in `E:\DEV\fm4dev\TrackStr\x`. Strumenti (x86, compilati con csc contro `bin\Debug\ForzaStudioEVO.exe`):
`ls`, `ex`, `ex2`, `db`, `tb`, `hi` (.cs) in `E:\DEV\fm4dev\TrackStr\`.

## Problema
Una tabella `.str` di un pacchetto a priorità più alta SOSTITUISCE tutta la tabella (Tracks, Environments,
ContentOffers, Data_Car...). Due pacchetti con la stessa tabella si nascondono i nomi a vicenda.
- Launch Track Pack ufficiale: porta ContentOffers, Environments, EnvironmentTimeOfDay, List_BadgeAndTitleUnlocks, Tracks.
- Il nostro `DlcPackage.BuildStrings`: tabelle cumulative sopra i livelli installati (`StringLayer`), ma un
  pacchetto installato DOPO nasconde ancora i nostri nomi.

## Il trucco (Bathurst)
- Il pacchetto contiene solo `StringTables_pri_30010.zip` con `<LANG>/Benchmark.str` (18 lingue).
- `stringtables.xml` elenca solo `<table Name="Benchmark" />`.
- `Benchmark.str` = copia cumulativa: 10 testi base del gioco + 10 propri (chiavi libere: 20010, 22083, 24417,
  39685, 43279, 49202, 52159, 52612, 58735, 61074 ...).
- Merge DB (`media/db/patch/6996000_merge.slt`): tutti i campi nome/descrizione (ContentOffers.DisplayName/Description,
  Environments.Location/DisplayName/ShortDisplayName/Description, EnvironmentTimeOfDay.Description,
  Tracks.DisplayName/ShortDisplayName) = `_&` + (0x7CE9 << 16 | chiave). Es. `_&2095664682` = 0x7CE9 . 20010.
- Benchmark è una tabella base che il DB non usa (la usa solo la UI del benchmark), nessun altro pacchetto la tocca.

## Fatti verificati
- I 16 bit alti di `_&N` = id della TABELLA, non della colonna (DB base: Data_Car.MakeName hi 3323 = List_CarMake;
  ContentOffers B8D0, Tracks C992, Data_Car CD49, Environments 09FA, EnvironmentTimeOfDay 94B7). Benchmark = 7CE9.
- Algoritmo nome tabella -> id NON trovato (crc32 varianti, fnv, jenkins, djb, sdbm, murmur3, con ASCII/UTF-16,
  maiuscole/minuscole, ".str", "IDS_"): nessuna corrispondenza, nemmeno per le chiavi (IDS_Stats -> 110E).
  Quindi si può usare solo 0x7CE9 (Benchmark), non nuove tabelle.
- Il collega dice "provato in Xenia 2026-10-19". Io NON l'ho provato in gioco.

## Limiti
- Restano collisioni solo tra pacchetti che usano entrambi Benchmark -> copia cumulativa dei livelli installati
  (`StringLayer.Merge` lo fa già per le altre tabelle).
- `StringTable.Lookup` (editor) risolve per nome tabella del DB: i riferimenti con hi 7CE9 non mostrano testo
  finché non si mappa 7CE9 -> Benchmark.

## Come il gioco carica stringhe e database (deduzione 2026-10-31, NON verificata in gioco)
Il `default.xex` e' compresso: nessuna stringa leggibile (merge, zipmount, patch), quindi e' dedotto solo dai file.
- Il gioco NON copia le stringtable degli altri DLC. Ogni pacchetto resta nella sua cartella; `zipmount.xml`
  dice quali zip montare e dove, la priorita' sta nel nome (`StringTables_pri_N.zip`).
- Ricerca per file: vince il montaggio a priorita' piu' alta, e il file sostituisce l'intera tabella.
  Non esiste un merge delle stringhe: le copie cumulative le fanno gli autori dei pacchetti (il `Data_Car.str`
  ufficiale = 1006 chiavi base + 186 proprie; da noi `StringLayer`, solo sui livelli gia' installati).
- Merge vero solo sul database: `db\patch\<offer>000_merge.slt` (SQLite con sole righe nuove, senza chiavi
  primarie) fuso all'avvio. Nessun comando SQL o script nei pacchetti, solo righe.
- NON noto: se una riga con Id gia' esistente viene sostituita o aggiunta.
- Perche' funziona Benchmark: il merge del DB agisce, la tabella .str di Benchmark non e' usata da nessun altro.

## Prove da fare con il DLC dell'utente
1. DLC + Bathurst installati insieme (due pacchetti Benchmark): si vedono i nomi di entrambi o solo del pri piu' alto?
2. Tabella normale (`Tracks`/`Data_Car`) nel DLC + pacchetto installato dopo: i nomi spariscono? (conferma la sostituzione per file)
3. Riga di test nel merge con Id gia' presente nel DB base: sostituisce o aggiunge?

## Da fare domani
1. Prova in gioco: un DLC auto con nomi in Benchmark, installato insieme al Bathurst (due pacchetti Benchmark:
   verifica la cumulatività).
2. `DlcPackage.BuildStrings`: scrivere nome/modello/descrizione (e make se serve) in `Benchmark.str`, chiavi libere
   in ogni lingua; `stringtables.xml` con solo `Benchmark`.
3. Merge DB: `_&(0x7CE9<<16 | chiave)` per DisplayName, ModelShort, ContentOffers, ecc. (vedi `CarPort` ~2102-2230,
   `CarMake`, `DlcPackage` ~89-110).
4. Adattare `DlcValidator` (controllo riferimenti `_&`, ~1226-1297), `DlcFixer` (~546), `CarUninstall`,
   `DlcCarDelete`, `DlcPackageAdd`, `StringTable.Lookup`.
5. Decidere se applicare solo ai DLC (come Bathurst) o anche alle installazioni locali.
