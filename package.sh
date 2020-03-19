#!/bin/bash
for csproj in ES.IoC/ES.IoC.csproj ES.IoC.Wiring/ES.IoC.Wiring.csproj
do
  dotnet pack -o . -c Release $csproj
done
