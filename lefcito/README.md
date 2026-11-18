# Lefcito · Liguilla de Left 4 Dead 2

Aplicación web para torneos de eliminación directa de 2 a 4 equipos. Registra marcadores parciales y finales, configura el mejor de 1, 3, 5, 7 o 9 rondas por fase y asigna puntaje por resultado y eventos individuales. Con tres equipos, el tercero pasa directo a la final.

## Arranque local

Con .NET 10 y PostgreSQL 17 instalados en Windows, ejecuta `.\Iniciar-Lefcito.ps1` desde este directorio. El script levanta una instancia aislada en loopback y conserva los datos en `.local`. Puedes indicar otra instalación de PostgreSQL con `-PostgresBin`. Mantén abierta la sesión del script mientras usas la web.

Si prefieres usar una instancia de PostgreSQL existente, configura una base exclusiva para Lefcito:

```powershell
$env:ConnectionStrings__Postgres = 'Host=localhost;Database=lefcito;Username=lefcito;Password=TU_CLAVE'
dotnet run -- --init-db
dotnet run -- --urls http://127.0.0.1:5180
```

Abre http://127.0.0.1:5180. La inicialización de la primera versión utiliza el esquema de EF Core. Crea una base vacía dedicada antes de inicializarla si el usuario de la aplicación carece de permisos para crear bases. La aplicación está configurada para uso local por el organizador. Requiere autenticación y una estrategia de migraciones antes de exponerla a terceros.

## Despliegue con Docker

- `Dockerfile.frontend`: Nginx sirve la interfaz y reenvía `/api/` al backend. Puerto interno `80`.
- `Dockerfile.backend`: ASP.NET Core 10 con EF Core y PostgreSQL. Puerto interno `8080`.
- `compose.yaml`: inicializa el esquema de Lefcito en PostgreSQL y arranca ambos contenedores. Usa una base dedicada vacía o ya inicializada para Lefcito.

### Docker Compose

Desde la carpeta `lefcito`, proporciona la cadena Npgsql como variable de tu terminal o como `LEFCITO_DB_CONNECTION` en un archivo local `.env` excluido de Git. Sustituye los valores de ejemplo por los de tu servidor. En Compose solo el backend y el inicializador reciben la cadena como secreto.

```dotenv
LEFCITO_DB_CONNECTION='Host=DB_HOST;Port=5432;Database=lefcito;Username=DB_USER;Password=DB_PASSWORD;SSL Mode=VerifyFull'
```

```bash
docker compose up -d --build
docker compose ps -a
docker compose logs init backend frontend
```

Abre http://127.0.0.1:8088 en la máquina que ejecuta Docker. Puedes cambiar el puerto con `LEFCITO_PORT`. El backend solo se comunica por la red interna de Docker. La interfaz permite modificar datos, por lo que para compartirla en Internet se requiere un proxy con HTTPS y control de acceso.

### EasyPanel

Crea dos servicios **App** dentro del mismo proyecto de EasyPanel que PostgreSQL. En ambos, usa el repositorio como origen, indica `/lefcito` en **Build Path** y selecciona el builder **Dockerfile**. Para un repositorio privado, configura en EasyPanel el acceso de lectura al repositorio.

| Servicio | Dockerfile | Puerto interno | Configuración |
| --- | --- | ---: | --- |
| Backend | `Dockerfile.backend` | 8080 | Monta un archivo privado con la cadena Npgsql y configura `ConnectionStrings__PostgresFile` con su ruta. Tiene que ser legible por el usuario del contenedor. Para la red interna usa `Host=DB_HOST;Port=5432;Database=lefcito;SSL Mode=Disable` y las credenciales de ese servicio. Elimina el dominio automático del backend si EasyPanel lo crea. |
| Frontend | `Dockerfile.frontend` | 80 | Configura `BACKEND_ADDRESS` con el nombre interno del backend y sufijo `:8080`. Asocia el dominio solo al frontend y protégelo con HTTPS y el control de acceso de EasyPanel. |

Si la base dedicada aún no tiene el esquema de Lefcito, ejecuta una vez `dotnet Lefcito.dll --init-db` desde la consola del backend. Luego comprueba `/api/jugadores` a través del frontend.

## Conexión a PostgreSQL

Ejecuta `.\Conectar-Postgres.ps1` con los parámetros de tu base para verificar la conexión sin escribir en PostgreSQL. PowerShell pide la contraseña con entrada oculta. El script requiere TLS con certificado válido. Si el certificado identifica un nombre DNS, indica ese nombre con `-DatabaseHost`. Para una autoridad privada usa `-RootCertificate`.

Para usar la conexión externa exactamente como aparece en EasyPanel, ejecuta `.\Conectar-Postgres.ps1 -DatabaseHost DB_HOST -DatabasePort 5432 -Database lefcito -SinTls`. Con esta opción el tráfico PostgreSQL viaja sin cifrado.

Si quieres cifrar el tráfico cuando el puerto externo no acepta TLS, abre un túnel SSH con un usuario autorizado del VPS. Mantén esa terminal abierta y ejecuta la comprobación desde otra terminal:

```powershell
ssh -N -L 127.0.0.1:55440:127.0.0.1:5432 USUARIO_SSH@VPS_HOST
.\Conectar-Postgres.ps1 -DatabaseHost 127.0.0.1 -DatabasePort 55440 -TunelSsh
```

Una vez verificada la conexión, puedes usar `-Inicializar -IniciarWeb` con una base dedicada y vacía o ya inicializada para Lefcito. La web conectada a PostgreSQL abre en http://127.0.0.1:5182. El perfil local sigue en el puerto 5180 y conserva sus jugadores y partidas.

## Enfrentamientos

Una partida de dos equipos permite añadir enfrentamientos con las mismas alineaciones. Cada enfrentamiento admite un formato al mejor de varias rondas o un marcador libre. En marcador libre, el ranking se actualiza con cada guardado. Los puntos por resultado se basan provisionalmente en el equipo que va ganando. Cerrar el enfrentamiento bloquea su marcador y eventos.

## Grupos

Al crear una partida o torneo, sus grupos se guardan para reutilizar. El nombre identifica al grupo sin distinguir mayúsculas. El selector recupera los jugadores de la partida o torneo más reciente con ese nombre. El ranking por grupo aplica los puntos de cada victoria o derrota una vez por enfrentamiento cerrado.

## Jugadores y ranking

Registra los nicknames desde la interfaz de Lefcito. El botón **Jugadores** registra nuevas personas y se pueden seleccionar al formar equipos. El **Ranking general** acumula las series cerradas de todas las partidas y torneos, incluido el historial anterior. Cada nickname es único e identifica al mismo jugador en todas las alineaciones.

## CQRS y persistencia

- El frontend usa JavaScript y consumos HTTP del mismo origen. ASP.NET Core sirve el frontend.
- MediatR enruta las consultas de lista y detalle y los comandos de creación y registro de marcador. El dominio controla avance, formato y participantes admitidos en cada cruce.
- EF Core y Npgsql guardan cada torneo como un agregado JSONB en PostgreSQL. La revisión evita sobrescrituras entre pestañas. El detalle genera el cuadro y la clasificación a partir del agregado.
- Los puntos de victoria o derrota se asignan por serie cerrada a cada jugador de la alineación. Los eventos suman cantidad × regla, también al cerrar la serie. Las alineaciones y el formato se eligen al crear el torneo. Puedes editar los valores y añadir reglas después de crearlo. Los cambios recalculan la clasificación y conservan los tipos de eventos ya utilizados. Un marcador se puede corregir, siempre que el cambio preserve cualquier ganador que ya figure en una final registrada.
