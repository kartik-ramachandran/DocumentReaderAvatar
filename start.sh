#!/bin/sh
dotnet /app/AvatarDocReader.Api.dll &
nginx -g 'daemon off;'
