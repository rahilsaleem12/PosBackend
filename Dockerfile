FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

RUN cat <<'EOF' > /etc/ssl/openssl.cnf
openssl_conf = openssl_init

[openssl_init]
ssl_conf = ssl_sect
providers = provider_sect

[ssl_sect]
system_default = system_default_sect

[system_default_sect]
MinProtocol = TLSv1.0
CipherString = DEFAULT@SECLEVEL=0

[provider_sect]
default = default_sect
legacy = legacy_sect

[default_sect]
activate = 1

[legacy_sect]
activate = 1
EOF

COPY ./Publish .
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet POS.API.dll"]