﻿using System;
using System.Collections.Generic;
using Mono.TextEditor;
//using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Extension;

namespace JustEnoughVi
{
    public abstract class ViMode
    {
        public TextEditorData Editor { get; set; }
        public Mode RequestedMode { get; internal set; }
        protected Dictionary<string , Func<int, char[], bool>> KeyMap { get; private set; }
        protected Dictionary<string , Command> CommandMap { get; private set; }

        private readonly List<char> _commandBuf;
        private int _count;
        private bool _countReset;
        private Command _command;
        private string _buf;

        protected ViMode(TextEditorData editor)
        {
            Editor = editor;
            RequestedMode = Mode.None;
            KeyMap = new Dictionary<string, Func<int, char[], bool>>();
            CommandMap = new Dictionary<string, Command>();

            _commandBuf = new List<char>();
            _buf = "";

            // standard motion keys
            KeyMap.Add("k", MotionUp);
            KeyMap.Add("j", MotionDown);
            KeyMap.Add("h", MotionLeft);
            KeyMap.Add("l", MotionRight);
            KeyMap.Add("^", MotionLineStart);
            KeyMap.Add("_", MotionLineStart);
            KeyMap.Add("$", MotionLineEnd);

            KeyMap.Add("^b", MotionPageUp);
            KeyMap.Add("^f", MotionPageDown);
            KeyMap.Add("^d", MotionPageDown);
            KeyMap.Add("^u", MotionPageUp);

            // remaps
            KeyMap.Add("Home", MotionFirstColumn);
            KeyMap.Add("End", MotionLineEnd);
            KeyMap.Add("Left", MotionLeft);
            KeyMap.Add("Right", MotionRight);
            KeyMap.Add("Up", MotionUp);
            KeyMap.Add("Down", MotionDown);
            KeyMap.Add("BackSpace", MotionLeft);
        }

        public void InternalActivate()
        {
            Reset();
            Activate();
        }

        public void InternalDeactivate()
        {
            Deactivate();
            Reset();
        }

        protected abstract void Activate();
        protected abstract void Deactivate();

        private void Reset()
        {
            _commandBuf.Clear();
            _count = 0;
            _countReset = false;
            _buf = "";
        }

        private void CaretOffEol()
        {
            if (RequestedMode == Mode.Insert)
                return;

            var line = Editor.GetLine(Editor.Caret.Line);
            if (line.EndOffset > line.Offset && Editor.Caret.Offset == line.EndOffset)
                CaretMoveActions.Left(Editor);
        }

        protected bool MotionFirstColumn(int count = 1, char[] args = null)
        {
            Editor.Caret.Column = Mono.TextEditor.DocumentLocation.MinColumn;
            return true;
        }

        protected bool MotionDown(int count = 1, char[] args = null)
        {
            count = Math.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                CaretMoveActions.Down(Editor);
            }

            return true;
        }

        protected bool MotionLeft(int count = 1, char[] args = null)
        {
            count = Math.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                if (DocumentLocation.MinColumn < Editor.Caret.Column)
                    CaretMoveActions.Left(Editor);
            }

            return true;
        }

        protected bool MotionUp(int count = 1, char[] args = null)
        {
            count = Math.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                CaretMoveActions.Up(Editor);
            }

            return true;
        }

        protected bool MotionRight(int count = 1, char[] args = null)
        {
            count = Math.Min(Math.Max(count, 1), Editor.GetLine(Editor.Caret.Line).EndOffset - Editor.Caret.Offset - 1);

            for (int i = 0; i < count; i++)
            {
                CaretMoveActions.Right(Editor);
            }

            return true;
        }

        protected bool MotionPageDown(int count = 1, char[] args = null)
        {
            Editor.Caret.Line += Math.Min(Editor.LineCount - Editor.Caret.Line, 20);
            CaretMoveActions.LineStart(Editor);
            Editor.CenterToCaret();
            return true;
        }

        protected bool MotionPageUp(int count = 1, char[] args = null)
        {
            Editor.Caret.Line -= Math.Min(Editor.Caret.Line - 1, 20);
            CaretMoveActions.LineStart(Editor);
            Editor.CenterToCaret();
            return true;
        }

        protected bool MotionLineEnd(int count = 1, char[] args = null)
        {
            CaretMoveActions.LineEnd(Editor);
            return true;
        }

        protected bool MotionLineStart(int count = 1, char[] args = null)
        {
            CaretMoveActions.LineStart(Editor);

            var line = Editor.GetLine(Editor.Caret.Line);

            while (Char.IsWhiteSpace(Editor.Text[Editor.Caret.Offset]) && Editor.Caret.Offset < line.EndOffset)
                   Editor.Caret.Offset++;

            return true;
        }

        private bool CommandKeyPress(KeyDescriptor descriptor)
        {
            // build repeat buffer
            if (_command == null && (_count > 0 || descriptor.KeyChar > '0') && descriptor.KeyChar >= '0' && descriptor.KeyChar <= '9')
            {
                _count = (_count * 10) + (descriptor.KeyChar - 48);
                return false;
            }
            // secondary run if command supports secondary count
            else if (_command != null && _command.SecondaryCount && descriptor.KeyChar >= '0' && descriptor.KeyChar <= '9')
            {
                if (!_countReset)
                {
                    _count = 0;
                    _countReset = true;
                }

                _count = (_count * 10) + (descriptor.KeyChar - 48);
                return false;
            }

            _buf += Char.ToString(descriptor.KeyChar);

            if (_command == null)
            {

                if (descriptor.ModifierKeys == ModifierKeys.Control)
                    _buf = "^" + _buf;

                if (!CommandMap.ContainsKey(_buf))
                {
                    if (_buf.Length > 2)
                    {
                        _count = 0;
                        _buf = "";
                        Reset();
                    }
                    return true;
                }

                _command = CommandMap[_buf];
                _buf = "";
                if (_command.TakeArgument)
                    return false;
            }

            CaretOffEol();

            RequestedMode = _command.RunCommand(_count, (char)(_buf.Length > 0 ? _buf[0] : 0));
            _command = null;
            Reset();

            CaretOffEol();
            return false;
        }

        public virtual bool KeyPress(KeyDescriptor descriptor)
        {
            // glue this here for now
            if (!CommandKeyPress(descriptor))
                return false;

            string command;

            if (descriptor.SpecialKey > 0)
            {
                command = descriptor.SpecialKey.ToString();
                _commandBuf.Clear();
                _count = 0;
            }
            else
            {
                // build repeat buffer
                if (_commandBuf.Count == 0 && (_count > 0 || descriptor.KeyChar > '0') && descriptor.KeyChar >= '0' && descriptor.KeyChar <= '9')
                {
                    _count = (_count * 10) + (descriptor.KeyChar - 48);
                    return false;
                }
                // secondary run if command is waiting for arguments
                else if (_commandBuf.Count == 1 && descriptor.KeyChar >= '0' && descriptor.KeyChar <= '9')
                {
                    if (!_countReset)
                    {
                        _count = 0;
                        _countReset = true;
                    }

                    _count = (_count * 10) + (descriptor.KeyChar - 48);
                    return false;
                }

                _commandBuf.Add(descriptor.KeyChar);
                command = Char.ToString(_commandBuf[0]);
            }

            if (descriptor.ModifierKeys == ModifierKeys.Control)
                command = "^" + command;

            if (!KeyMap.ContainsKey(command))
            {
                Reset();
                return false;
            }

            CaretOffEol();

            if (KeyMap[command](_count, (_commandBuf.Count > 1 ? _commandBuf.GetRange(1, _commandBuf.Count - 1).ToArray() : new char[] { })))
            {
                Reset();
            }

            CaretOffEol();

            return false;
        }

        // TODO: move this somewhere else? extend TextEditor?
        protected void SetSelectLines(int start, int end)
        {
            start = Math.Min(start, Editor.LineCount);
            end = Math.Min(end, Editor.LineCount);

            var startLine = start > end ? Editor.GetLine(end) : Editor.GetLine(start);
            var endLine = start > end ? Editor.GetLine(start) : Editor.GetLine(end);

            Editor.SetSelection(startLine.Offset, endLine.EndOffsetIncludingDelimiter);
        }
    }
}

