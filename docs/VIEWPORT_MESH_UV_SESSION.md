# Viewport, Mesh editor, UV Mapper: stato della sessione

Data: 2026-10-31. File per riprendere il lavoro (TODO D-20 viewport, D-21 mesh editor + UV mapper).
Stato: tutto compilato e provato dall'utente a vista ("direi che ci siamo!"); restano le prove di editing e il test in gioco. Niente è stato committato (l'utente fa il push).

> **Aggiornamento 2026-11-05.** Tutto committato e pushato (tag v0.0.1). Viste, telecamere live, MESH, UV mapper e
> collisioni chiusi dall'utente il 2026-11-04. Le anteprime delle telecamere **esterne** (cofano, paraurti) sono state
> **spente**: il gioco somma gli offset di `CameraOverrides` a un suo punto base che non è in nessun file, e l'anteprima
> metteva l'occhio dentro l'auto (NSX '97). Restano cockpit (verificato in gioco) e thumbnail. Il punto 1 qui sotto è
> la richiesta originale del 31/10.

## 1. Richieste dell'utente (riassunto)

1. **Rivoluzione del viewport**
   - Le viste dalle telecamere esterne (cofano, paraurti, cockpit, thumbnail) funzionano e si vedono in tempo reale ("live") mentre si cambiano i valori delle caselle numeriche.
   - Viste standard sui tasti 1-6 (tastierino e riga di cifre), ordine alla Blender: Front, Back, Right, Left, Top, Bottom.
   - Modo ortografico; orbita, pan e zoom come in 3ds Max.
   - Guardia sul focus: il tastierino non scrive mai nelle caselle.
2. **Collision**: pulsante "Generate Basic Collision" per le collisioni rotte (es. Agera FH1).
3. **Mesh editor**: selezionare facce, delete, detach (nello stesso nodo/parte, non in un nodo nuovo).
4. **UV Mapper**: canale UV1/UV2; partenza da canale corrente, altro canale, mondo o viewport; seleziona/sposta/ruota/scala; toolbar con icone, hint e shortcut; sfondo texture/canale/check; anteprima live nel viewport con la texture scelta; sessioni multi-mesh scelte con Ctrl o per materiale.
5. **Menu tasto destro** stile 3ds Max, click per selezionare la mesh nell'albero, Ctrl per la multiselezione.

## 2. Vincoli tecnici

- C# WinForms, **.NET Framework 4.8** dal 2026-11-01 (prima 2.0: per questo il codice vecchio usa `Set<T>` di `Set2.cs` invece di HashSet), x86, XNA 3.1 (Matrix a vettori riga, BasicEffect, clip z D3D 0..1).
- Ogni file nuovo va elencato in `ForzaStudioEVO.csproj`.
- Build con MSBuild di VS2022 in `bin\Debug`. Se l'exe è aperto la build fallisce (MSB3021/3027): chiedere all'utente di chiuderlo, mai ucciderlo.
- Harness headless: `E:\DEV\fm4dev\MeshEdit\meshtest.cs` (csc 4.0, serve `meshtest.exe.config` da colsurvey e il riferimento XNA 3.1 dal GAC).
- Convenzione UV: valori raw D3D, V cresce verso il basso; nel viewer `texture0 = (u, 1-v)`; OBJ `vt = 1 - V`.
- Script: niente heredoc bash con virgolette, attenzione ai fine riga misti (rompono i replace multi-riga); per i file usare Edit/Write.

## 3. File principali (tutti in ForzaStudio\)

| File | Contenuto |
|---|---|
| Camera.cs | Ortho (Orthographic, OrthoHeight, ViewportHeight), SetStandardView, SetOrthographic(on, focus, halfDepth), Orbit(dAz, dEl, pivot) con offset del pivot mantenuto negli assi camera, Dolly, FocusOn, PanPixels |
| ArchiveEditorForm.ViewControl.cs | KeyViews, toolbar viste/ortho/strumenti mesh, HandleToolKey (Delete, L, P, U, Ctrl+A, Ctrl+Shift+A), HandleViewKey (tastierino con puntatore sul viewport; cifre solo con focus sul viewport e non in una casella), ModelBounds, OrbitPivot, FocusIsTextEntry |
| ArchiveEditorForm.Model.cs | Toolbar (Wireframe, Textures, Camera speed, slider FOV, Go to Camera) e mouse: rotella = zoom (ortho: OrthoHeight, prospettiva: dolly), tasto centrale = pan, Alt+centrale = orbita, click sinistro = seleziona mesh (fuori dal tab Mesh), tasto destro = menu |
| ToolIcons.cs | Icone procedurali 22x22 (viste, ortho, selezione, linked, delete, detach, UV e strumenti UV) |
| CarbinMeshEdit.cs | TriangleStrips, CopyVertexToEnd, CompactSection, DeleteFaces, DetachFaces (sub copiata nella stessa sezione, indici divisi, nuova voce nelle draw list dopo la sorgente) |
| ArchiveEditorForm.MeshEdit.cs | Tab Mesh: selezione facce (`Set<long>`), box select (opzione "solo facce frontali"), Linked, overlay, Delete/Detach, ApplyFaceEdit (ordine decrescente per mesh); salta mesh Hidden/Frozen |
| ArchiveEditorForm.ClickPick.cs | SelectMeshAt(p, add, keepIfSelected): triangolo più vicino tra le mesh visibili non congelate; Ctrl alterna in `meshSelection`; click nel vuoto deseleziona |
| ArchiveEditorForm.ViewMenu.cs | Menu tasto destro: Show Only This / End Show Only, Unfreeze All, Freeze Selection, Unhide by Name…, Unhide All, Hide Unselected, Hide Selection, Focus On Selected |
| ArchiveEditorForm.MaterialCheck.cs / Materials.cs | TreeSelected() (albero → `meshSelection`, Ctrl aggiunge/toglie), HighlightSelection() (niente alone mentre l'UV mapper è aperto); SelectedMeshes ritorna `meshSelection` se non vuota; tab Mesh (indice 6) |
| UvSession.cs | Create (SplitShared copia i vertici condivisi, ricalcola Positions e fa Grow di U,V,U0,V0 al conteggio finale: fix del crash multi-mesh), MapFromOtherChannel/World(asse)/View, Transform, Island, Snapshot/Restore/Reset, Commit (allarga l'intervallo sub.Uv se serve), BuildPreview/UpdatePreview |
| UvMapperForm.cs | Toolbar icone (Q W E R, A, Ctrl+D, L, H flip U, V flip V, T ruota 90, Shift+F fit, Ctrl+Z, Ctrl+Shift+R, F), barra texture (texture, canali, check grigio/colorato, ForChannels, ReadFile), evento `TextureChanged`, info a destra, Apply/Cancel |
| ArchiveEditorForm.UvMapperOpen.cs | OpenUvMapper (mesh da selezione facce o SelectedMeshes, dialogo canale/partenza/split/lista mesh con materiale e All/None), BuildPreview in try/catch con reload, texture sul viewport (ForceTexture), chiusura (Apply scrive via zip.Replace + ReloadModelKeepingView; Cancel ricarica), CloseUvMapperForModelChange |
| ArchiveEditorForm.Camera.cs / ExteriorCamera.cs / ThumbCamera.cs | chkCamLive, LiveCameraView, ExteriorEye (cofano da CameraOverrides, paraurti stimato), overlay cofano/paraurti; il cockpit non tocca più il combo FOV |
| ArchiveEditorForm.Collision.cs + CollisionGenerate.cs | btnCollGenerate "Generate Basic Collision...", box + sfere da modello mostrato |
| ForzaMesh.cs, RenderScene.cs, Set2.cs | Invalidate, Frozen (grigio, senza texture), ForceTexture; Set<T> con comparer |

## 4. Regole e modelli

- **Collision FM4**: da physics/maxdata.xml, Z in avanti dal centro passo (Misc/BottomCenterWheelbasePos). Scatola: semi-larghezza = semi-larghezza carrozzeria − 0.018, tetto = alto carrozzeria − 0.010, fronte = muso mesh − 0.077, retro = coda mesh + 0.095, fondo 0.006, smussi 0.611/0.744 (larghezza) e 0.819/0.805 (lunghezza). Layout FM4 con 32 sfere.
- **Mouse come 3ds Max**: rotella zoom, centrale pan mantenendo il punto sull'oggetto, Alt+centrale orbita, click seleziona, tasto destro menu.
- **UV**: raw u16 a offset 8 + set*4, scalati da `sub.Uv[set*4..+3] = offU, scaleU, offV, scaleV`.

## 5. Correzioni dell'utente da ricordare

- La toolbar con icone va nell'**UV Mapper**, non nella schermata del viewer; il viewport principale non si tocca se non richiesto.
- Il detach resta nello stesso nodo/parte.
- La camera non deve scattare a 0,0,0 mentre si orbita: pivot con offset mantenuto, base esatta.
- Ortho → prospettiva non deve saltare più vicino né perdere il FOV: `halfDepth` al lato vicino del modello.
- Rotella in prospettiva = dolly (zoom), non FOV; il FOV è solo uno slider (disabilitato in ortho).
- Nell'UV mapper niente alone di selezione; la texture scelta si vede sul viewport (ForceTexture) e all'uscita torna l'originale.
- Multiselezione mesh per UV mapper (es. tutto il materiale body); il crash all'apertura con più mesh è risolto con il Grow in UvSession.Create.

## 6. Rischi aperti / da verificare

- **Detach**: due sub con stesso materiale e LOD nella stessa parte potrebbero non essere accettate dal gioco → test in gioco.
- Camera paraurti: stima, non dato reale.
- Layout di Generate Basic Collision: approssimato; controllare da sopra e di lato.
- Le modifiche toccano solo il LOD della mesh selezionata.
- Box select ignora l'occlusione (c'è solo l'opzione facce frontali).
- Un vertice condiviso tra due mesh tiene un solo intervallo UV.
- Icona bianca della finestra non fatta.

## 7. Prossimi passi

1. Prove di editing dell'utente: raccogliere crash o comportamenti strani e correggere.
2. Aggiornare TODO.md (D-20, D-21) con le funzioni successive: schema mouse, toolbar, barra texture, menu tasto destro, slider FOV, multiselezione.
3. Commit solo su richiesta dell'utente (non è mai stato fatto per questo lavoro).
