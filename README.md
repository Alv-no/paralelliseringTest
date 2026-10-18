# AIS Voyage

Et rederi mottar en kontinuerlig strøm av AIS-posisjonsmeldinger fra flåten sin. Hver melding inneholder fartøy-ID, tidspunkt, posisjon og fart. Systemet beregner tilbakelagt distanse og estimert drivstofforbruk per fartøy, som grunnlag for utslippsrapportering.

Drivstoffberegningen er CPU-tung, så prosesseringen er parallellisert over flere workers.

## Problemet

Totalsummene varierer fra kjøring til kjøring, og stemmer ikke med resultatet fra en kjøring med én worker.

## Oppgaven

Finn årsaken og fiks den uten å miste parallelliseringen. Alle testene skal passere, uten at testene endres.

## Kom i gang

```
dotnet test
```

Krever .NET 8. Har du bare en nyere .NET-runtime installert, sett miljøvariabelen `DOTNET_ROLL_FORWARD=Major`.
