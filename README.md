# Claude Council Manager

App desktop Windows (WPF, .NET 10) per configurare e avviare **concili**: gruppi di sessioni
interattive di [Claude Code](https://code.claude.com) che lavorano insieme sullo stesso progetto e
si scambiano messaggi.

- Un **esperto** è una cartella con il suo `CLAUDE.md`, le sue skill e un `expert.json`, ad
  esempio `DEV-BE`, `QA` o `TEST-E2E`. Si versiona su git e si condivide con i colleghi.
- Un **concilio** è una cartella di lavoro (il progetto) più un gruppo di esperti. Con due o più
  esperti si aggiunge in automatico il **coordinatore**: riceve il tuo obiettivo, lo divide tra gli
  esperti e raccoglie i risultati.
- **«Avvia»** apre una sessione per ogni membro (di default una tab di Windows Terminal).
  **«Riprendi»** riapre le ultime conversazioni. **■** termina le sessioni del concilio.

## Requisiti

| | |
|---|---|
| Sistema | Windows 10 o 11 |
| Claude Code | **2.1.234 o successivo**, installato e con login fatto. Verifica con `claude --version`. Le versioni precedenti non hanno la messaggistica tra sessioni. |
| PowerShell | **PowerShell 7.3 o successivo** (`pwsh`): le sessioni partono sempre lì, con la versione più recente installata. Installalo con `winget install Microsoft.PowerShell`. Windows PowerShell 5.1 non basta. |
| Terminale | [Windows Terminal](https://aka.ms/terminal), consigliato: ogni concilio diventa una finestra con una tab per membro. In alternativa, dalle **Impostazioni** puoi usare finestre PowerShell separate o un altro terminale. |
| Build | .NET 10 SDK |

## Onboarding

Questo percorso usa gli esperti della cartella [`Example`](Example): un coordinatore (`LEAD`), `QA`,
`DEV-BE`, `DEV-FE`, `TEST-BE`, `TEST-FE` e `TEST-E2E`.

### 1. Avvia l'app

```powershell
dotnet run
```

In alternativa apri `ClaudeCouncilManager.sln` in Visual Studio. Al primo avvio l'app crea il suo
database in `%LOCALAPPDATA%\ClaudeCouncilManager`.

### 2. Prepara la tua cartella di esperti

Copia `Example` in una cartella tua, per esempio `C:\Esperti`. È meglio se è un repository git, così
puoi condividerla con il team. Non usare `Example` direttamente: quando modifichi un esperto
nell'app, l'app aggiorna il suo `expert.json`, e così modificheresti gli esempi del repo.

### 3. Importa gli esperti

Scheda **Esperti** → **Importa esperti…** → scegli `C:\Esperti`.

L'anteprima elenca le 7 cartelle trovate e segnala che `LEAD` diventerà il coordinatore. Premi
**Importa 7 esperti**.

### 4. Personalizza gli esperti

Seleziona un esperto per modificarlo nel pannello a destra:

- **Modifica CLAUDE.md**: sono le istruzioni dell'esperto (ruolo, perimetro, cosa consegnare). Gli
  esempi sono generici: adattali al tuo stack e alle tue convenzioni.
- **Modello / Effort**: vuoti significa che si usano i valori predefiniti di Claude Code. Per
  esempio puoi dare `opus · high` al coordinatore e `sonnet` ai tester.
- **Apri cartella skill**: aggiungi le skill dell'esperto, una sottocartella per skill con il suo
  `SKILL.md`.
- **Coordinatore dei concili**: ce n'è uno solo. Spuntarlo su un esperto lo toglie al precedente.

Le modifiche vengono salvate subito, sia nel database sia nell'`expert.json` dell'esperto.

### 5. Crea un concilio

Scheda **Concili** → **Nuovo concilio**. Il pulsante si abilita solo quando esistono un
coordinatore e almeno un altro esperto.

1. Scegli la **cartella di lavoro**: è il progetto su cui lavorerà il concilio, per esempio il
   clone del repo. Tutte le sessioni partono da qui. Il nome del concilio viene proposto dal nome
   della cartella.
2. Spunta gli esperti, per esempio `DEV-BE`, `DEV-FE` e `QA`. `LEAD` si aggiunge da solo come
   coordinatore.
3. Facoltativo: scrivi un **prompt iniziale** per il coordinatore, ad esempio *«Aggiungi
   l'esportazione CSV degli ordini, con test»*.

### 6. Avvia il concilio

Premi **▶ Avvia**:

- si apre una finestra di Windows Terminal con una tab per membro;
- il coordinatore parte per ultimo, quando gli altri sono già raggiungibili, e riceve il prompt
  iniziale;
- la prima volta che una cartella di lavoro viene usata, ogni tab chiede se ti fidi della
  cartella: conferma.

Da qui lavori nella tab del coordinatore. Lui delega agli esperti con dei messaggi e loro gli
rispondono. Puoi anche scrivere direttamente nella tab di un esperto. Sulla card del concilio,
**● aperta** indica le sessioni effettivamente in esecuzione.

### 7. Termina e riprendi

- **■** sulla card termina tutte le sessioni aperte del concilio. Le conversazioni restano salvate.
  Nella conferma, la casella **«Chiudi anche le finestre/tab del terminale»** decide se chiudere
  anche le tab. Se non la spunti, al posto di Claude resta il prompt di PowerShell nella cartella di
  lavoro. L'app ricorda l'ultima scelta.
- Per un solo membro, usa il tasto destro sul suo nome nella card e scegli **Termina la sessione**.
- Puoi anche chiudere le tab a mano o scrivere `/exit`.
- **⟲ Riprendi** riapre l'ultima conversazione di ogni membro.
- Un clic su un singolo membro della card riapre solo la sua sessione.
- **▶ Avvia** invece inizia conversazioni nuove.

## Com'è fatto un esperto

```
DEV-BE\
├── CLAUDE.md                    istruzioni dell'esperto (obbligatorio)
├── expert.json                  impostazioni condivisibili (vedi sotto)
└── .claude\
    ├── skills\<nome>\SKILL.md   skill dell'esperto (facoltative)
    └── settings.json            permessi dell'esperto (facoltativo)
```

```json
{
  "name": "LEAD",
  "description": "Coordinatore: pianifica, delega agli esperti e sintetizza i risultati",
  "color": "#D97757",
  "model": "opus",
  "effort": "high",
  "isCoordinator": true
}
```

- `model` e `effort` sono facoltativi.
- La `description` viene presentata agli altri membri del concilio, quindi conviene scriverla bene.
- I **preferiti** (♥) non finiscono in `expert.json`, perché sono una scelta personale.

Le sessioni non partono nella cartella dell'esperto: partono nella cartella di lavoro del
concilio. `CLAUDE.md`, skill e `settings.json` dell'esperto vengono caricati dalla sua cartella.
Così le cartelle degli esperti restano pura configurazione e gli agenti non ci scrivono dentro.

## Condividere gli esperti con il team

1. Metti le cartelle degli esperti in un repository git aziendale. Aggiungi un `.gitignore` per i
   file personali che Claude Code crea nelle cartelle:
   ```
   **/.claude/settings.local.json
   **/CLAUDE.local.md
   ```
2. Un collega clona il repository e usa **Importa esperti…**.
3. Dopo un `git pull`, premi **Aggiorna** nella scheda Esperti: l'app rilegge `expert.json`,
   `CLAUDE.md` e skill.

L'app riscrive l'`expert.json` solo degli esperti che hai modificato tu, quindi non sovrascrive
gli aggiornamenti arrivati da git. Il flag coordinatore del collega viene preso solo all'import,
e solo se tu non hai ancora un coordinatore.

## Come comunicano le sessioni

L'app non fa da intermediario: usa la **messaggistica nativa tra sessioni** di Claude Code (vedi
la [documentazione](https://code.claude.com/docs/en/cross-session-messaging)).

- Ogni sessione si chiama `CONCILIO.ESPERTO`, per esempio `SHOP.DEV-BE`: è l'indirizzo che gli
  altri membri usano. Nella tab, `/peers` mostra chi è online.
- Nei concili con più membri, ogni sessione riceve un **protocollo** generato dall'app. Il
  protocollo dice chi è il membro, chi sono gli altri, chi coordina, il formato dei messaggi
  (`[TASK-n] richiesta|risposta|domanda|stato: …`) e qual è la cartella condivisa per i file
  lunghi.
- Le **istruzioni aggiuntive** del concilio vengono aggiunte in coda al protocollo.

## Dove salva i dati

| Cosa | Dove |
|---|---|
| Esperti, concili, preferiti, sessioni da riprendere | `%LOCALAPPDATA%\ClaudeCouncilManager\claudecouncil.db` (SQLite) |
| Protocolli generati e cartella condivisa di ogni concilio | `%LOCALAPPDATA%\ClaudeCouncilManager\runtime\<CONCILIO>\` |
| Conversazioni | gestite da Claude Code in `%USERPROFILE%\.claude\projects\` |

Le versioni precedenti si chiamavano *ClaudeCouncil* e salvavano i dati in
`%LOCALAPPDATA%\ClaudeCouncil`. Al primo avvio la nuova versione li copia nella nuova cartella. La
vecchia cartella resta come backup e la puoi cancellare.

## Impostazioni

Nella scheda **Impostazioni** scegli dove si aprono le sessioni. Ogni sessione gira sempre in
PowerShell 7.

- **Windows Terminal** (predefinito): una finestra per concilio, una tab per membro con il nome e il
  colore dell'esperto.
- **Finestra PowerShell**: una finestra separata per ogni membro.
- **Comando personalizzato**: un altro terminale. Indichi il programma e gli argomenti, con i
  segnaposto `{pwsh} {pwshArgs} {workdir} {title} {council} {expert} {color}`. Gli argomenti
  devono contenere `"{pwsh}" {pwshArgs}`, che avviano la sessione. Esempio per WezTerm:
  programma `wezterm`, argomenti `start --cwd "{workdir}" -- "{pwsh}" {pwshArgs}`.

PowerShell viene rilevato da solo: l'app sceglie la versione stabile più recente installata. In
alternativa puoi indicare il percorso di `pwsh.exe`.

Quando esci da `claude` con `/exit`, la tab o la finestra si chiude. Se `claude` si chiude con un
errore, la tab resta aperta sul prompt di PowerShell, così puoi leggere il messaggio.

## Area di notifica

- Se riduci a icona la finestra, l'app va nell'area di notifica.
- Un clic sull'icona la riapre; dal menu del tasto destro ci sono **Apri** ed **Esci**.
- La **X** della finestra chiude l'app.

## Problemi comuni

- **«Il concilio non può essere avviato»**: il messaggio dice cosa manca. Di solito è la cartella
  di lavoro, il coordinatore, o una cartella esperto che non esiste più.
- **I messaggi tra sessioni non arrivano o restano in attesa**: tutti i membri devono usare la
  stessa modalità di permessi. Se una sessione salta le conferme e l'altra no, Claude Code trattiene
  i messaggi finché non li approvi.
- **Hai chiuso una tab per errore**: clicca il membro sulla card del concilio. Riprende la sua
  conversazione.
- **«PowerShell 7 non trovato» o «Windows Terminal non trovato»**: installa quello che manca,
  oppure cambia modalità o percorso nelle **Impostazioni**.
- **Non succede niente premendo «Avvia»**: controlla che `claude` funzioni in un terminale e che la
  versione sia almeno 2.1.234.

## Sviluppo

L'architettura e le note per chi sviluppa sono in [`CLAUDE.md`](CLAUDE.md).

```powershell
dotnet build
dotnet tool restore                                   # dotnet-ef locale (dotnet-tools.json)
dotnet ef migrations add <Nome> --output-dir Data/Migrations
```
