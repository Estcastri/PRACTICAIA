using System;
using System.Collections.Generic;
using NavigationDJIA.World;

/// <summary>
/// TODO(alumno):
/// Define el "estado" que usará la Tabla Q para identificar cada situación del agente.
///
/// El estado debe contener toda la información necesaria para que el agente pueda
/// tomar decisiones informadas. Tú decides qué características incluir según lo
/// que consideres relevante para resolver el problema.
///
/// Ejemplos típicos de información que puede formar un estado:
///   - Posición del agente en la grid.
///   - Posición del otro personaje (enemigo).
///   - Distancia relativa entre agente y enemigo.
///   - Si hay muros en direcciones cercanas.
///   - Cualquier otro dato que consideres útil.
///
/// En este ejercicio te damos un ejemplo simple basado únicamente en las posiciones
/// del agente y del oponente. Puedes usarlo tal cual o ampliarlo.
///
/// IMPORTANTE:
///  El estado debe poder convertirse a una clave única (string) mediante ToKey(),
///  ya que esa clave se usará como índice en la TablaQ y en el archivo CSV.
/// </summary>

namespace GrupoA
{
    /// <summary>
    /// Estado del agente para la Tabla Q.
    ///
    /// DISEÑO (independiente de la posición absoluta y de la distribución de muros):
    /// Para cada una de las 5 acciones (Up, Down, Left, Right, Stay) se calcula una CATEGORÍA:
    ///   0 = bloqueada (muro o fuera del tablero)
    ///   1 = mortal    (tras mover, el perseguidor llega a la celda)
    ///   2/3/4 = segura, con calidad baja/media/alta según el territorio de escape del destino:
    ///           número de celdas que el agente alcanza ANTES que el perseguidor, contando solo celdas del 2-core
    ///           del grafo.
    ///           Si ninguna acción tiene territorio de 2-core se usa el territorio total.
    ///           La calidad es RELATIVA al mejor destino: alta si &gt;= 85 %, media si &gt;= 50 %.
    /// Además, se añade la distancia de camino al perseguidor, saturada: 1, 2, 3, 4+ ; 5 = inalcanzable.
    ///
    /// Clave: "c_up c_down c_left c_right c_stay | distBucket"   p. ej. "42431|3"
    /// Como máximo 5^5 * 5 estados.
    /// </summary>
    public sealed class QState
    {
        // Valor de distancia para celdas inalcanzables.
        private const int Inf = 9999;

        // Número de acciones posibles (Up, Down, Left, Right, Stay).
        private const int NumActions = 5;

        // Desplazamientos por acción. Orden = enum QAction: Up, Down, Left, Right, Stay.
        private static readonly int[] DX = { 0, 0, -1, 1, 0 };
        private static readonly int[] DY = { 1, -1, 0, 0, 0 };

        // ---- Caché estático del mundo, no cambia en ejecución ----
        private static WorldInfo _world; // mundo para el que se calculó la caché
        private static int _w, _h; // ancho y alto del tablero
        private static bool[] _walk; // true si la celda es caminable
        private static bool[] _core; // true si la celda pertenece al 2-core, no es rama muerta
        private static int[] _queue; // cola reutilizable para el BFS
        private static int[] _distChaser; // distancias de camino desde el perseguidor
        private static int[] _distTmp; // distancias de camino desde un destino candidato

        /// <summary>
        /// Categoría (0-4) de cada una de las 5 acciones, en el orden Up, Down, Left, Right, Stay.
        /// </summary>
        public int[] ActionCategories { get; }

        /// <summary>
        /// Distancia de camino al perseguidor saturada a 4, 5 si es inalcanzable.
        /// </summary>
        public int ChaserDistanceBucket { get; }

        /// <summary>
        /// Prepara la información estática del mundo: celdas caminables y celdas del 2-core.
        /// </summary>
        /// <param name="world">Información del escenario.</param>
        public static void SetWorld(WorldInfo world)
        {
            if (world == null) throw new InvalidOperationException("QState: WorldInfo no disponible.");
            if (ReferenceEquals(world, _world)) return;

            _world = world;
            _w = (int)world.WorldSize.x;
            _h = (int)world.WorldSize.y;
            int n = _w * _h;

            // Guarda qué celdas son caminables.
            _walk = new bool[n];
            for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
                _walk[x + _w * y] = world[x, y].Walkable;

            // Buffers reutilizables para los BFS.
            _queue = new int[n];
            _distChaser = new int[n];
            _distTmp = new int[n];

            _core = ComputeTwoCore();
        }

        /// <summary>
        /// Calcula el 2-core del grafo de celdas caminables. Elimina de forma repetida las celdas
        /// con un vecino caminable o ninguno (pasillos sin salida y ramas muertas).
        /// </summary>
        /// <returns>Array de bool (índice = x + ancho * y). true si la celda pertenece al 2-core.</returns>
        private static bool[] ComputeTwoCore()
        {
            int n = _w * _h;
            bool[] alive = (bool[])_walk.Clone();
            int[] deg = new int[n];
            var stack = new Stack<int>();

            // Grado inicial de cada celda, las de grado <= 1 se eliminarán.
            for (int i = 0; i < n; i++)
            {
                if (!alive[i]) continue;
                deg[i] = CountWalkNeighbours(i);
                if (deg[i] <= 1) stack.Push(i);
            }

            // Se elimina cada celda y se reduce el grado de sus vecinos, si alguno baja a <= 1 también se elimina.
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (!alive[i]) continue;
                alive[i] = false;
                int cx = i % _w, cy = i / _w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (!InBounds(nx, ny)) continue;
                    int j = nx + _w * ny;
                    if (!_walk[j] || !alive[j]) continue;
                    deg[j]--;
                    if (deg[j] <= 1) stack.Push(j);
                }
            }

            return alive;
        }

        /// <summary>
        /// Cuenta cuántos de los 4 vecinos de una celda son caminables.
        /// </summary>
        /// <param name="i">Índice de la celda (x + ancho * y).</param>
        /// <returns>Número de vecinos caminables (entre 0 y 4).</returns>
        private static int CountWalkNeighbours(int i)
        {
            int cx = i % _w, cy = i / _w, count = 0;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + DX[k], ny = cy + DY[k];
                if (InBounds(nx, ny) && _walk[nx + _w * ny]) count++;
            }

            return count;
        }

        /// <summary>
        /// Comprueba si una coordenada está dentro del tablero.
        /// </summary>
        /// <param name="x">Coordenada x.</param>
        /// <param name="y">Coordenada y.</param>
        /// <returns>true si (x, y) está dentro del tablero, false en caso contrario.</returns>
        private static bool InBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < _w && y < _h;
        }

        /// <summary>
        /// Comprueba si una coordenada está dentro del tablero y no es un muro.
        /// </summary>
        /// <param name="x">Coordenada x.</param>
        /// <param name="y">Coordenada y.</param>
        /// <returns>true si la celda es caminable, false si es muro o está fuera del tablero.</returns>
        private static bool IsWalkable(int x, int y)
        {
            return InBounds(x, y) && _walk[x + _w * y];
        }

        /// <summary>
        /// Búsqueda en anchura (4 direcciones) sobre celdas caminables.
        /// Rellena el array con los pasos mínimos desde la celda de inicio hasta cada celda.
        /// </summary>
        /// <param name="start">Índice de la celda de inicio (x + ancho * y).</param>
        /// <param name="dist">Array de salida. Inf en las celdas inalcanzables.</param>
        private static void Bfs(int start, int[] dist)
        {
            for (int i = 0; i < dist.Length; i++) dist[i] = Inf;
            int head = 0, tail = 0;
            dist[start] = 0;
            _queue[tail++] = start;
            while (head < tail)
            {
                int cur = _queue[head++];
                int cx = cur % _w, cy = cur / _w;
                int nd = dist[cur] + 1;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (!InBounds(nx, ny)) continue;
                    int ni = nx + _w * ny;
                    if (!_walk[ni] || dist[ni] != Inf) continue;
                    dist[ni] = nd;
                    _queue[tail++] = ni;
                }
            }
        }

        /// <summary>
        /// Constructor corto: construye el estado usando el mundo registrado previamente con SetWorld.
        /// </summary>
        /// <param name="agent">Celda del agente.</param>
        /// <param name="other">Celda del perseguidor.</param>
        public QState(CellInfo agent, CellInfo other) : this(agent, other, _world)
        {
        }

        /// <summary>
        /// Constructor principal: calcula la categoría de cada acción y la distancia al perseguidor.
        /// </summary>
        /// <param name="agent">Celda del agente.</param>
        /// <param name="other">Celda del perseguidor.</param>
        /// <param name="world">Información del escenario.</param>
        public QState(CellInfo agent, CellInfo other, WorldInfo world)
        {
            SetWorld(world);

            int a = agent.x + _w * agent.y;
            int c = other.x + _w * other.y;

            // Distancias de camino desde el perseguidor a todas las celdas.
            Bfs(c, _distChaser);
            int distCA = _distChaser[a];

            var cat = new int[NumActions];
            var terrCore = new int[NumActions]; // territorio contando solo celdas del 2-core
            var terrAll = new int[NumActions]; // territorio contando todas las celdas

            for (int k = 0; k < NumActions; k++)
            {
                terrCore[k] = -1;
                terrAll[k] = -1;

                int nx = agent.x + DX[k];
                int ny = agent.y + DY[k];

                if (k < 4 && !IsWalkable(nx, ny))
                {
                    cat[k] = 0; // muro o fuera del tablero
                    continue;
                }

                int dest = nx + _w * ny; // para Stay coincide con la celda actual
                if (_distChaser[dest] <= 1)
                {
                    cat[k] = 1; // el perseguidor alcanza en su turno al agente
                    continue;
                }

                // Territorio de escape: celdas que el agente alcanza antes que el perseguidor.
                Bfs(dest, _distTmp);
                int tc = 0, ta = 0;
                for (int i = 0; i < _distTmp.Length; i++)
                {
                    if (_distTmp[i] < _distChaser[i])
                    {
                        ta++;
                        if (_core[i]) tc++;
                    }
                }

                terrCore[k] = tc;
                terrAll[k] = ta;
                cat[k] = 2; // segura (calidad provisional)
            }

            // Se usa el territorio del 2-core; si ninguna acción tiene, se usa el territorio total.
            int[] terr = terrCore;
            int tmax = -1;
            for (int k = 0; k < NumActions; k++)
                if (terr[k] > tmax)
                    tmax = terr[k];
            if (tmax <= 0)
            {
                terr = terrAll;
                tmax = -1;
                for (int k = 0; k < NumActions; k++)
                    if (terr[k] > tmax)
                        tmax = terr[k];
            }

            // Calidad relativa de las acciones seguras respecto a la mejor.
            if (tmax > 0)
            {
                for (int k = 0; k < NumActions; k++)
                {
                    if (cat[k] < 2) continue;
                    int t = terr[k];
                    // t/tmax >= 0.85  <=>  100 t >= 85 tmax ;  t/tmax >= 0.5  <=>  2 t >= tmax
                    cat[k] = (100 * t >= 85 * tmax) ? 4 : ((2 * t >= tmax) ? 3 : 2);
                }
            }

            ActionCategories = cat;
            ChaserDistanceBucket = distCA >= Inf ? 5 : Math.Min(distCA, 4);
        }

        /// <summary>
        /// Convierte el estado en la clave de texto usada como índice de la tabla Q.
        /// </summary>
        /// <returns>
        /// Cadena con el formato "cUp cDown cLeft cRight cStay | distancia".
        /// </returns>
        /// <example>"12342|2"</example>
        public string ToKey()
        {
            return
                $"{ActionCategories[0]}{ActionCategories[1]}{ActionCategories[2]}{ActionCategories[3]}{ActionCategories[4]}|{ChaserDistanceBucket}";
        }
    }
}