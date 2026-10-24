using System;
using QMind;

namespace GrupoA
{
    /// <summary>
    /// Tabla de calidad Q(s,a). Utiliza un Dictionary&lt;string, float[]&gt;:
    /// clave = estado (QState.ToKey()), valor = array de 5 floats (Up, Down, Left, Right, Stay).
    /// Los estados no vistos se crean con Q = 0.
    /// </summary>
    public class QTable
    {
        // Almacenamiento con el diccionario de datos y la persistencia.
        private readonly QTableStorage _storage;

        // Nombres de las acciones (Up, Down, Left, Right, Stay).
        private readonly string[] _actionNames;

        /// <summary>
        /// Crea la tabla sobre un almacenamiento ya existente o carga el CSV si existe.
        /// </summary>
        /// <param name="storage">Almacenamiento con el diccionario y la persistencia.</param>
        public QTable(QTableStorage storage)
        {
            _storage = storage;
            _actionNames = Enum.GetNames(typeof(QAction));
        }

        /// <summary>
        /// Garantiza que el estado existe en la tabla. Si no existe, crea su fila con todos los valores a 0.
        /// </summary>
        /// <param name="stateKey">Clave del estado.</param>
        private void EnsureState(string stateKey)
        {
            if (!_storage.Data.ContainsKey(stateKey))
            {
                _storage.Data[stateKey] = new float[_actionNames.Length];
            }
        }

        /// <summary>
        /// TODO(alumno):
        /// Devuelve el valor Q(s, a) correspondiente al estado y acción indicados.
        ///
        /// Pasos recomendados:
        ///  1. Asegúrate de que el estado existe llamando a EnsureState(stateKey).
        ///  2. Convierte la acción en un índice del array:
        ///        int index = (int)action;
        ///  3. Devuelve el valor almacenado en:
        ///        _storage.Data[stateKey][index]
        /// </summary>
        /// <param name="stateKey">Clave del estado s.</param>
        /// <param name="action">Acción a.</param>
        /// <returns>El valor Q(s, a) almacenado (0 si el estado no se había visto).</returns>
        public float GetQ(string stateKey, QAction action)
        {
            EnsureState(stateKey);
            return _storage.Data[stateKey][(int)action];
        }

        /// <summary>
        /// TODO(alumno):
        /// Asigna el valor Q(s, a) para el estado y acción indicados.
        /// 
        /// Pasos recomendados:
        ///  1. Asegúrate de que el estado existe llamando a EnsureState(stateKey).
        ///  2. Convierte la acción en un índice del array:
        ///        int index = (int)action;
        ///  3. Guarda el valor recibido en:
        ///        _storage.Data[stateKey][index] = value;
        /// </summary>
        /// <param name="stateKey">Clave del estado s.</param>
        /// <param name="action">Acción a.</param>
        /// <param name="value">Nuevo valor de Q(s, a).</param>
        public void SetQ(string stateKey, QAction action, float value)
        {
            EnsureState(stateKey);
            _storage.Data[stateKey][(int)action] = value;
        }

        /// <summary>
        /// TODO(alumno):
        /// Devuelve el valor máximo max_a Q(s, a) para el estado indicado.
        /// 
        /// Este método se usa en la actualización de Q-Learning:
        ///   maxQNext = GetMaxQ(nextStateKey)
        ///
        /// Pasos recomendados:
        ///  1. Asegúrate de que el estado existe llamando a EnsureState(stateKey).
        ///  2. Obtén el array de Q-values:
        ///        var qValues = _storage.Data[stateKey];
        ///  3. Recorre el array buscando el valor máximo y devuélvelo.
        /// </summary>
        /// <param name="stateKey">Clave del estado s.</param>
        /// <returns>El mayor valor Q entre todas las acciones del estado.</returns>
        public float GetMaxQ(string stateKey)
        {
            EnsureState(stateKey);
            float[] qValues = _storage.Data[stateKey];
            float max = qValues[0];
            for (int i = 1; i < qValues.Length; i++)
            {
                if (qValues[i] > max) max = qValues[i];
            }

            return max;
        }

        /// <summary>
        /// TODO(alumno):
        /// Devuelve la mejor acción para el estado indicado:
        ///    argmax_a Q(s, a)
        /// 
        /// Este método se usa para:
        ///   - Política greedy (explotar lo aprendido).
        ///   - Parte "explotar" de la política ε-greedy.
        /// 
        /// Pasos recomendados:
        ///  1. Asegúrate de que el estado existe llamando a EnsureState(stateKey).
        ///  2. Obtén el array de Q-values:
        ///        var qValues = _storage.Data[stateKey];
        ///  3. Recorre el array buscando el índice del valor máximo.
        ///  4. Convierte ese índice a QAction:
        ///        return (QAction)bestIndex;
        /// </summary>
        /// <param name="stateKey">Clave del estado s.</param>
        /// <returns>La acción (QAction) con mayor valor Q en ese estado.</returns>
        public QAction GetBestAction(string stateKey)
        {
            EnsureState(stateKey);
            float[] qValues = _storage.Data[stateKey];
            int bestIndex = 0;
            for (int i = 1; i < qValues.Length; i++)
            {
                if (qValues[i] > qValues[bestIndex]) bestIndex = i;
            }

            return (QAction)bestIndex;
        }

        /// <summary>
        /// Guarda la tabla Q en el fichero CSV.
        /// </summary>
        public void SaveToCsv()
        {
            _storage.Save();
        }
    }
}