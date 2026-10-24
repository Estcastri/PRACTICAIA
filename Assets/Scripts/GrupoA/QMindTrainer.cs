using System;
using NavigationDJIA.Interfaces;
using NavigationDJIA.World;
using QMind;
using QMind.Interfaces;

namespace GrupoA
{
    /// <summary>
    /// Entrenador de Q-Learning. Ejecuta episodios en los que el agente huye del perseguidor,
    /// elige acciones con exploración (epsilon-greedy), calcula recompensas y actualiza la tabla Q
    /// con Q(s,a) = (1 - alpha) Q(s,a) + alpha (r + gamma max Q(s',·)).
    /// </summary>
    public class QMindTrainer : IQMindTrainer
    {
        // ---- Recompensas ----
        private const float RewardSurvive = 1f; // por cada paso en el que el agente no es capturado
        private const float RewardCaptured = -100f; // el perseguidor alcanza al agente
        private const float RewardBump = -5f; // intentar entrar en un muro o salir del tablero

        // ---- Entrenamiento ----
        private const float EpsilonMin = 0.05f; // suelo de exploración
        private const float EpsilonDecayFraction = 0.7f; // epsilon decae linealmente durante un % de los episodios
        private const int DefaultMaxSteps = 500; // tope de pasos por episodio si _params.maxSteps <= 0

        private QMindTrainerParams _params; // hiperparámetros (alpha, epsilon, gamma, episodios...)
        private WorldInfo _worldInfo; // información del escenario
        INavigationAlgorithm _navigationAlgorithm; // algoritmo A* que mueve al perseguidor

        private QTableStorage _qStorage; // almacenamiento de la tabla
        private QTable _qTable; // tabla de calidad

        private CellInfo _agentPosition; // posición actual del agente
        private CellInfo _otherPosition; // posición actual del perseguidor

        private float _return; // retorno acumulado del episodio
        private float _returnAveraged; // recompensa media por paso del episodio
        private System.Random _random = new System.Random();

        private bool _finished; // true cuando se han completado todos los episodios
        private bool _dirty; // true si la tabla cambió desde el último guardado

        #region IQMindTrainer implementation

        public CellInfo AgentPosition => _agentPosition;
        public CellInfo OtherPosition => _otherPosition;

        public int CurrentEpisode { get; private set; }
        public int CurrentStep { get; private set; }

        public float Return => _return;
        public float ReturnAveraged => _returnAveraged;

        public event EventHandler OnEpisodeStarted;
        public event EventHandler OnEpisodeFinished;

        #endregion

        /// <summary>
        /// Prepara el entrenador: guarda parámetros y mundo, registra el mundo en QState,
        /// carga TablaQ.csv si existe e inicia el primer episodio.
        /// </summary>
        /// <param name="qMindTrainerParams">Hiperparámetros del algoritmo.</param>
        /// <param name="worldInfo">Información del escenario.</param>
        /// <param name="navigationAlgorithm">Algoritmo de navegación del perseguidor.</param>
        public void Initialize(QMindTrainerParams qMindTrainerParams, WorldInfo worldInfo, INavigationAlgorithm navigationAlgorithm)
        {
            _params = qMindTrainerParams;
            _worldInfo = worldInfo;
            _navigationAlgorithm = navigationAlgorithm;
            _navigationAlgorithm.Initialize(worldInfo);

            QState.SetWorld(worldInfo);

            _qStorage = new QTableStorage("TablaQ.csv");
            _qTable = new QTable(_qStorage);

            _finished = false;
            _dirty = false;
            CurrentEpisode = 0;
            StartNewEpisode();
        }

        /// <summary>
        /// Empieza un episodio nuevo: reinicia contadores y coloca al agente y al perseguidor
        /// en celdas aleatorias, caminables y distintas.
        /// </summary>
        private void StartNewEpisode()
        {
            CurrentEpisode++;
            CurrentStep = 0;
            _return = 0f;
            _returnAveraged = 0f;

            const int retries = 1000;
            CellInfo agent = _worldInfo.RandomCell();
            CellInfo other = _worldInfo.RandomCell();
            for (int tries = 0; tries < retries; tries++)
            {
                if (agent.Walkable && other.Walkable && !SameCell(agent, other)) break;
                agent = _worldInfo.RandomCell();
                other = _worldInfo.RandomCell();
            }

            _agentPosition = agent;
            _otherPosition = other;

            OnEpisodeStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Termina el episodio actual: guarda la tabla si procede, avisa con OnEpisodeFinished y empieza otro episodio
        /// o termina el entrenamiento.
        /// </summary>
        private void EndEpisode()
        {
            bool lastEpisode = _params.episodes > 0 && CurrentEpisode >= _params.episodes;
            int every = Math.Max(1, _params.episodesBetweenSaves);

            if (_dirty && (lastEpisode || _params.episodes <= 0 || CurrentEpisode % every == 0))
            {
                _qTable.SaveToCsv();
                _dirty = false;
            }

            OnEpisodeFinished?.Invoke(this, EventArgs.Empty);

            if (lastEpisode)
            {
                _finished = true;
                return;
            }

            StartNewEpisode();
        }

        /// <summary>
        /// Ejecuta un turno completo: construye el estado, elige acción, mueve al agente y al perseguidor,
        /// calcula el nuevo estado y la recompensa, actualiza la tabla Q y comprueba si el episodio termina.
        /// </summary>
        /// <param name="train">Si es false no se actualiza la tabla Q y el agente no explora.</param>
        public void DoStep(bool train)
        {
            if (_finished) return;

            // Estado actual del agente.
            string stateKey = BuildStateKey(_agentPosition, _otherPosition, _worldInfo);

            // Selecciona la acción a realizar.
            QAction action = ChooseAction(stateKey, train);

            // Nuevas posiciones del agente y del oponente.
            CellInfo target = ApplyAction(_agentPosition, action);
            bool bumped = target is null || !target.Walkable;
            CellInfo newAgentPos = bumped ? _agentPosition : target;
            CellInfo newOtherPos = MoveOpponent(_otherPosition, newAgentPos);

            // Nuevo estado del agente.
            string nextStateKey = BuildStateKey(newAgentPos, newOtherPos, _worldInfo);

            bool terminal = IsTerminalState(newAgentPos, newOtherPos);

            // Calcula la recompensa.
            float reward = ComputeReward(newAgentPos, newOtherPos, bumped);

            if (train)
            {
                UpdateQ(stateKey, action, reward, nextStateKey, terminal);
            }

            // Actualiza las posiciones.
            _agentPosition = newAgentPos;
            _otherPosition = newOtherPos;

            // Actualiza las estadísticas de recompensa.
            CurrentStep++;
            _return += reward;
            _returnAveraged = (_returnAveraged * (CurrentStep - 1) + reward) / CurrentStep;

            // Comprobación de si estamos en el fin de episodio.
            if (terminal || CurrentStep >= EffectiveMaxSteps())
            {
                EndEpisode();
            }
        }

        #region Parte a implementar por el alumno

        /// <summary>
        /// Construye la clave de estado a partir de las posiciones del agente y del perseguidor.
        /// Debe ser igual a la de QMindTester.
        /// </summary>
        /// <param name="agent">Celda del agente.</param>
        /// <param name="other">Celda del perseguidor.</param>
        /// <param name="_worldInfo">Información del escenario.</param>
        /// <returns>Cadena con la clave del estado, p. ej. "12342|2".</returns>
        private string BuildStateKey(CellInfo agent, CellInfo other, WorldInfo _worldInfo)
        {
            var state = new QState(agent, other, _worldInfo);
            return state.ToKey();
        }

        /// <summary>
        /// Ejemplo orientativo:
        ///    - Si train == false, puedes usar la mejor acción.
        ///    - Si train == true, con probabilidad epsilon elegir acción aleatoria,
        ///      y con probabilidad 1-epsilon la mejor según _qTable.GetBestAction(stateKey).
        /// </summary>
        /// <param name="stateKey">Clave del estado actual.</param>
        /// <param name="train">true durante el entrenamiento (con exploración).</param>
        /// <returns>La acción (QAction) que debe ejecutar el agente.</returns>
        private QAction ChooseAction(string stateKey, bool train)
        {
            if (!train)
            {
                return _qTable.GetBestAction(stateKey);
            }

            // Exploración: acción aleatoria.
            if (_random.NextDouble() < CurrentEpsilon())
            {
                return (QAction)_random.Next(Enum.GetValues(typeof(QAction)).Length);
            }

            // Explotación: mejor acción, con desempate aleatorio entre las que igualan el máximo.
            float maxQ = _qTable.GetMaxQ(stateKey);
            int count = 0;
            int numActions = Enum.GetValues(typeof(QAction)).Length;
            for (int i = 0; i < numActions; i++)
            {
                if (_qTable.GetQ(stateKey, (QAction)i) >= maxQ) count++;
            }

            int pick = _random.Next(count);
            for (int i = 0; i < numActions; i++)
            {
                if (_qTable.GetQ(stateKey, (QAction)i) >= maxQ)
                {
                    if (pick == 0) return (QAction)i;
                    pick--;
                }
            }

            return _qTable.GetBestAction(stateKey);
        }

        /// <summary>
        /// Actualización de Q-Learning:
        /// Q(s,a) = (1 - alpha) * Q(s,a) + alpha * (reward + gamma * max_a' Q(s',a')).
        /// Usa _qTable.GetQ, _qTable.SetQ y _qTable.GetMaxQ.
        /// </summary>
        /// <param name="stateKey">Clave del estado s.</param>
        /// <param name="action">Acción a realizada en s.</param>
        /// <param name="reward">Recompensa r obtenida.</param>
        /// <param name="nextStateKey">Clave del estado siguiente s'.</param>
        /// <param name="terminal">true si s' es un estado terminal.</param>
        private void UpdateQ(string stateKey, QAction action, float reward, string nextStateKey, bool terminal)
        {
            float oldQ = _qTable.GetQ(stateKey, action);
            float target = terminal ? reward : reward + _params.gamma * _qTable.GetMaxQ(nextStateKey);
            float newQ = (1f - _params.alpha) * oldQ + _params.alpha * target;
            _qTable.SetQ(stateKey, action, newQ);
            _dirty = true;
        }

        /// <summary>
        /// Función de recompensa.
        /// Ejemplo orientativo:
        ///   si agent == other -> recompensa positiva grande (captura)
        ///   si no -> pequeña penalización negativa por cada paso.
        /// </summary>
        /// <param name="agent">Nueva celda del agente.</param>
        /// <param name="other">Nueva celda del perseguidor.</param>
        /// <param name="bumped">true si la acción elegida llevaba a un muro o fuera del tablero.</param>
        /// <returns>La recompensa del paso: -100, -5 o +1.</returns>
        private float ComputeReward(CellInfo agent, CellInfo other, bool bumped)
        {
            if (SameCell(agent, other)) return RewardCaptured;
            if (bumped) return RewardBump;
            return RewardSurvive;
        }

        /// <summary>
        /// Condición de final de episodio.
        /// Lo más simple: cuando agente y oponente están en la misma celda.
        /// También puedes definir una probabilidad para el parámetro v visto en clase.
        /// </summary>
        /// <param name="agent">Celda del agente.</param>
        /// <param name="other">Celda del perseguidor.</param>
        /// <returns>true si el agente ha sido capturado; false en caso contrario.</returns>
        private bool IsTerminalState(CellInfo agent, CellInfo other)
        {
            return SameCell(agent, other);
        }

        /// <summary>
        /// Calcula la celda a la que llevaría una acción desde la celda del agente.
        /// </summary>
        /// <param name="agentCell">Celda actual del agente.</param>
        /// <param name="action">Acción a aplicar.</param>
        /// <returns>
        /// La celda destino o null si el destino queda fuera del tablero.
        /// </returns>
        private CellInfo ApplyAction(CellInfo agentCell, QAction action)
        {
            int nx = agentCell.x;
            int ny = agentCell.y;

            switch (action)
            {
                case QAction.Up: ny += 1; break;
                case QAction.Down: ny -= 1; break;
                case QAction.Right: nx += 1; break;
                case QAction.Left: nx -= 1; break;
                case QAction.Stay: return agentCell;
            }

            if (nx < 0 || ny < 0 || nx >= (int)_worldInfo.WorldSize.x || ny >= (int)_worldInfo.WorldSize.y)
            {
                return null;
            }

            return _worldInfo[nx, ny];
        }

        /// <summary>
        /// Mueve al perseguidor un paso hacia el agente.
        /// </summary>
        /// <param name="opponent">Celda actual del perseguidor.</param>
        /// <param name="target">Celda objetivo, nueva posición del agente.</param>
        /// <returns>La nueva celda del perseguidor o la misma si no hay camino.</returns>
        private CellInfo MoveOpponent(CellInfo opponent, CellInfo target)
        {
            var path = _navigationAlgorithm.GetPath(opponent, target, 1);
            if (path.Length > 0)
                return path[0];

            return opponent;
        }

        /// <summary>
        /// Compara dos celdas por sus coordenadas.
        /// </summary>
        /// <param name="a">Primera celda.</param>
        /// <param name="b">Segunda celda.</param>
        /// <returns>true si ambas celdas tienen las mismas coordenadas x e y.</returns>
        private static bool SameCell(CellInfo a, CellInfo b)
        {
            return a.x == b.x && a.y == b.y;
        }

        /// <summary>
        /// Obtiene el número máximo de pasos por episodio: el del Inspector o,
        /// si vale -1 o menos de 1, el valor por defecto DefaultMaxSteps.
        /// </summary>
        /// <returns>El tope de pasos por episodio.</returns>
        private int EffectiveMaxSteps()
        {
            return _params.maxSteps > 0 ? _params.maxSteps : DefaultMaxSteps;
        }

        /// <summary>
        /// Calcula el epsilon del episodio actual: parte del valor del Inspector y decrece linealmente
        /// hasta EpsilonMin durante un % de los episodios.
        /// </summary>
        /// <returns>La probabilidad de exploración, entre EpsilonMin y el epsilon inicial.</returns>
        private float CurrentEpsilon()
        {
            float e0 = _params.epsilon;
            if (_params.episodes <= 0) return e0;

            float progress = CurrentEpisode / (EpsilonDecayFraction * _params.episodes);
            float eps = e0 * (1f - progress);
            float floor = Math.Min(EpsilonMin, e0);
            return Math.Max(floor, eps);
        }

        #endregion
    }
}