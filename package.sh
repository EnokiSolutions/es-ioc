#!/bin/bash
for nuspec in $(find . -name '*.nuspec')
do
  csproj=${nuspec/nuspec/csproj}
  nuget pack $csproj -Prop Configuration=Release
done
