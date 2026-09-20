# Langu

Traduttore a schermo offline per Windows. Tieni premuto un tasto, leggi il testo sullo schermo (latino e CJK) e clicca per tradurre.

Autore: [Creiv](https://github.com/Creiv)

## Requisiti

- Windows 10/11 64 bit
- .NET 8 SDK per compilare
- Per la traduzione offline: modelli NLLB (pulsante **Scarica modelli** nell’app)

## Download

Release pronta: [Langu 1.0.0](https://github.com/Creiv/Langu/releases/tag/v1.0.0)

1. Scarica [Langu-1.0.0-win-x64.zip](https://github.com/Creiv/Langu/releases/download/v1.0.0/Langu-1.0.0-win-x64.zip)
2. Estrai e avvia `Langu.exe`
3. In Langu: **Avvia**. Per tradurre offline usa **Scarica modelli**

Non serve installare .NET.

## Compilare

1. Compila in Release, oppure pubblica un eseguibile autonomo:

```bat
dotnet publish src\Langu.App\Langu.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\Langu
```

2. Avvia `dist\Langu\Langu.exe`
3. In Langu: **Avvia**, tieni premuto il tasto sonda, clic sinistro su una box, destro su tutte

## Uso

- Tasto sonda (predefinito Alt): mostra le box OCR
- Click sinistro: traduce una box
- Click destro: traduce tutte le box
- Rotella: seleziona un’area da rileggere
- Doppio tap sul tasto: cancella overlay e interrompe OCR/traduzioni

## Test

```bat
dotnet test src\Langu.Tests\Langu.Tests.csproj -c Debug
dotnet run --project src\Langu.App\Langu.App.csproj -- --smoke
```

Pagina di prova: `tests/langu-test.html`

## Licenza

Usa e modifica a tuo rischio. I modelli OCR e NLLB restano sotto le licenze dei rispettivi autori.
