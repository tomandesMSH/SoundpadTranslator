# Soundpad Translator

Druhá klávesnice jako soundboard pro [Soundpad](https://leppsoft.com/soundpad/).
Klávesy z ní se nedostanou do Windows. Místo toho přehrají zvuk přes Soundpad Remote Control API.
První klávesnice funguje normálně.

## Instalace

1. Sestavení: `dotnet build -c Release src\SoundpadTranslator`
   (výstup najdeš v `src\SoundpadTranslator\bin\Release\net10.0-windows\win-x64\`).
2. Spusť `SoundpadTranslator.exe`. Když chybí driver Interception, aplikace nabídne
   **Nainstalovat ovladač** (potvrdíš v UAC) a pak restart.
   Ruční odinstalace: `install-interception.exe /uninstall` jako administrátor a restart.
3. Spusť Soundpad a znovu `SoundpadTranslator.exe`.

> **Důležité:** Pro správnou funkci smaž v Soundpadu všechny klávesové zkratky (hotkeys):
> u každého zvuku s hotkey dej pravý klik -> **Remove hotkey**. Jinak můžou kolidovat s klávesami soundboardu.

## Použití

Při prvním spuštění se aplikace zeptá na jazyk (čeština / English). Později ho změníš v hlavním okně v poli **Jazyk**.

1. Při prvním spuštění se otevře okno "Stiskni klávesu na klávesnici, kterou chceš používat jako soundboard".
   Stiskni klávesu na druhé klávesnici a potvrď **Použít tuto klávesnici**.
   Klávesnici můžeš kdykoli změnit tlačítkem **Zvolit klávesnici...**.
2. **Přidat klávesu...**, stiskni klávesu na soundboard klávesnici a vyber zvuk (nebo Stop / Pauza).
3. Zavřením okna aplikaci schováš do oznamovací oblasti. Ukončíš ji pravým klikem na ikonu a volbou Ukončit.

Nastavení se ukládá do `%APPDATA%\SoundpadTranslator\config.json`.
Zvuky se pamatují podle cesty k souboru, takže přeuspořádání seznamu v Soundpadu přiřazení nerozbije.

## Poznámky

- Interception je ovladač na úrovni jádra. Anticheaty jako Vanguard (Valorant) nebo FACEIT ho můžou blokovat nebo hlásit.
- Interception má 10 slotů pro klávesnice. Po mnoha odpojeních a připojeních USB přestane nové zařízení
  zachytávat, dokud nerestartuješ počítač.
- Klávesa Num Lock na soundboard klávesnici se taky spolkne. Stav Num Lock je sdílený s hlavní klávesnicí,
  ale na scancodech, které aplikace používá, nezáleží.
- Interception (oblitum) je pro nekomerční použití pod LGPL 3.0, viz `tools/`.
