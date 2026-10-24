using System;
using NavigationDJIA.World;
using QMind;
using QMind.Interfaces;

namespace GrupoA
{
    /// <summary>
    /// Agente que juega con la tabla Q ya entrenada. No explora ni aprende:
    /// en cada turno elige la mejor acción de TablaQ.csv para el estado actual.
    /// </summary>
    public class QMindTester : IQMind
    {
        private WorldInfo _worldInfo; // información del escenario
        private QTableStorage _qStorage; // almacenamiento de la tabla Q
        private QTable _qTable; // tabla de calidad

        /// <summary>
        /// Prepara el agente: guarda el mundo, lo registra en QState y carga TablaQ.csv.
        /// </summary>
        /// <param name="worldInfo">Información del escenario.</param>
        public void Initialize(WorldInfo worldInfo)
        {
            _worldInfo = worldInfo;

            QState.SetWorld(worldInfo);

            _qStorage = new QTableStorage("TablaQ.csv");
            _qTable = new QTable(_qStorage);
        }

        /// <summary>
        /// Decide el siguiente movimiento del agente: toma la mejor acción de la tabla Q para el estado actual.
        /// Si esa acción llevara a un muro, elige la mejor acción caminable.
        /// </summary>
        /// <param name="currentPosition">Celda actual del agente.</param>
        /// <param name="otherPosition">Celda actual del perseguidor.</param>
        /// <returns>La celda a la que debe moverse el agente.</returns>
        public CellInfo GetNextStep(CellInfo currentPosition, CellInfo otherPosition)
        {
            string stateKey = BuildStateKey(currentPosition, otherPosition, _worldInfo);

            QAction bestAction = _qTable.GetBestAction(stateKey);

            CellInfo nextPosition = ApplyAction(currentPosition, bestAction);

            // Si la mejor acción lleva a un muro, se toma la mejor acción caminable.
            if (nextPosition is null || !nextPosition.Walkable)
            {
                float bestQ = float.NegativeInfinity;
                QAction bestWalkable = QAction.Stay;
                foreach (QAction action in Enum.GetValues(typeof(QAction)))
                {
                    CellInfo candidate = ApplyAction(currentPosition, action);
                    if (candidate is null || !candidate.Walkable) continue;

                    float q = _qTable.GetQ(stateKey, action);
                    if (q > bestQ)
                    {
                        bestQ = q;
                        bestWalkable = action;
                    }
                }

                nextPosition = ApplyAction(currentPosition, bestWalkable);
            }

            return nextPosition;
        }

        /// <summary>
        /// Construye la clave de estado a partir de las posiciones del agente y del perseguidor.
        /// Debe ser igual a la de QMindTrainer.
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
                default: return _worldInfo[agentCell.x, agentCell.y];
            }

            if (nx < 0 || ny < 0 || nx >= (int)_worldInfo.WorldSize.x || ny >= (int)_worldInfo.WorldSize.y)
            {
                return null;
            }

            return _worldInfo[nx, ny];
        }
    }
}