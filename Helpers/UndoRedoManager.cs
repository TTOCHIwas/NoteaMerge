using System;
using System.Collections.Generic;

namespace Notea.Helpers
{
    public class UndoRedoManager<T> where T : class
    {
        private readonly Stack<T> _undoStack = new();
        private readonly Stack<T> _redoStack = new();
        private readonly int _maxStackSize;

        public UndoRedoManager(int maxStackSize = 50)
        {
            _maxStackSize = maxStackSize;
        }

        public void AddState(T state)
        {
            _undoStack.Push(state);
            _redoStack.Clear(); // 새로운 액션 시 redo 스택 클리어

            // 스택 크기 제한
            if (_undoStack.Count > _maxStackSize)
            {
                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = temp.Length - _maxStackSize; i < temp.Length; i++)
                {
                    _undoStack.Push(temp[i]);
                }
            }
        }

        public T Undo()
        {
            if (_undoStack.Count > 1) // 최소 1개는 남겨둠 (현재 상태)
            {
                var current = _undoStack.Pop();
                _redoStack.Push(current);
                return _undoStack.Peek();
            }
            return null;
        }

        public T Redo()
        {
            if (_redoStack.Count > 0)
            {
                var state = _redoStack.Pop();
                _undoStack.Push(state);
                return state;
            }
            return null;
        }

        public bool CanUndo => _undoStack.Count > 1;
        public bool CanRedo => _redoStack.Count > 0;
    }
}




