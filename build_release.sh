#!/bin/sh
nuget restore
msbuild /p:Configuration=Release
