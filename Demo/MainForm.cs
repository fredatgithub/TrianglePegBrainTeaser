﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using FlowSharpLib;
using FlowSharpServiceInterfaces;

using Clifton.Core.ExtensionMethods;

using BrainTeaser;

namespace Demo
{
	public partial class MainForm : Form
	{
		public const int NUM_ROWS = 5;

		protected Board board;
		protected Algorithm algorithm;
		protected int solutionStep;
		protected List<Hop> solutionHops;
		protected bool running;
		protected bool singleStepping;
		protected int startPosition = 0;

		public MainForm()
		{
			InitializeComponent();
			InitializeFlowSharp();
			Shown += OnShown;
			FormClosing += OnFormClosing;
		}

		private void OnFormClosing(object sender, FormClosingEventArgs e)
		{
			WebSocketHelpers.Close();
		}

		protected void InitializeFlowSharp()
		{
			var canvasService = Program.ServiceManager.Get<IFlowSharpCanvasService>();
			canvasService.CreateCanvas(pnlFlowSharp);
			canvasService.ActiveController.Canvas.EndInit();
			canvasService.ActiveController.Canvas.Invalidate();

			// Initialize Toolbox so we can drop shapes
			IFlowSharpToolboxService toolboxService = Program.ServiceManager.Get<IFlowSharpToolboxService>();

			// We don't display the toolbox, but we need a container.
			Panel pnlToolbox = new Panel();
			pnlToolbox.Visible = false;
			Controls.Add(pnlToolbox);

			toolboxService.CreateToolbox(pnlToolbox);
			toolboxService.InitializeToolbox();

			var mouseController = Program.ServiceManager.Get<IFlowSharpMouseControllerService>();
			mouseController.Initialize(canvasService.ActiveController);
		}

		protected void OnShown(object sender, EventArgs e)
		{
			board = new Board(NUM_ROWS);
			// algorithm = new IterativeAlgorithm(board);
			algorithm = new RecursiveYieldAlgorithm(board);
			board.DoHop += OnHop;
			board.UndoHop += OnUndoHop;

			WebSocketHelpers.ClearCanvas();
			DrawBoard();
			ShowPegs();
		}

		/// <summary>
		/// Update UI.
		/// </summary>
		protected void OnHop(object sender, CellChangeEventArgs e)
		{
			if (ckShowUi.Checked)
			{
				WebSocketHelpers.UpdateProperty("c" + e.Hop.FromCellIndex, "FillColor", "White");
				WebSocketHelpers.UpdateProperty("c" + e.Hop.HoppedCellIndex, "FillColor", "White");
				WebSocketHelpers.UpdateProperty("c" + e.Hop.ToCellIndex, "FillColor", board.GetPegColor(e.Hop.ToCellIndex).Name);
				UpdateUI();
			}
		}

		protected void OnUndoHop(object sender, CellChangeEventArgs e)
		{
			if (ckShowUi.Checked)
			{
				WebSocketHelpers.UpdateProperty("c" + e.Hop.FromCellIndex, "FillColor", board.GetPegColor(e.Hop.FromCellIndex).Name);
				WebSocketHelpers.UpdateProperty("c" + e.Hop.HoppedCellIndex, "FillColor", board.GetPegColor(e.Hop.HoppedCellIndex).Name);
				WebSocketHelpers.UpdateProperty("c" + e.Hop.ToCellIndex, "FillColor", "White");
				UpdateUI();
			}
		}

		protected void UpdateUI()
		{
			// We only call DoEvents and delay when the algorithm is running.  If single stepping
			// or perusing the solution, we do NOT want to call DoEvents because additional mouse events
			// could then fire and the state of the game gets messed up.
			if (running)
			{
				lbSolution.DataSource = algorithm.UndoStack.Reverse().ToList();
				Application.DoEvents();
				System.Threading.Thread.Sleep(tbIterateDelay.Text.to_i());
			}
		}

		protected void DrawBoard()
		{
			int cx = (pnlFlowSharp.Width - 50)/2;
			int y = 50;
			int n = 0;

			Rectangle trect = new Rectangle(cx - NUM_ROWS * 50 + 65, y - 15, NUM_ROWS * 50 + 100, y + NUM_ROWS * 50 + 25);
			WebSocketHelpers.DropShape("UpTriangle", "t", trect, Color.FromArgb(240, 230, 140));

			NUM_ROWS.ForEach(row =>
			{
				row.ForEach(cell =>
				{
					string name = "c" + n;
					Rectangle rect = new Rectangle(cx - 25 * row + 50 * cell, y + row * 50, 30, 30);
					WebSocketHelpers.DropShape("Ellipse", name, rect, Color.White);
					WebSocketHelpers.UpdateProperty(name, "Text", n.ToString());
					++n;
				});
			}, 1);
		}

		protected void ShowPegs()
		{
			board.NumCells.ForEach(n => WebSocketHelpers.UpdateProperty("c" + n, "FillColor", board.GetPegColor(n).Name));
			WebSocketHelpers.UpdateProperty("c" + startPosition, "FillColor", "White");
		}

		private void btnRun_Click(object sender, EventArgs e)
		{
			ShowPegs();
			algorithm.Initialize(startPosition);
			running = true;
			solutionHops?.Clear();
			lbSolution.DataSource = new List<Hop>();
			algorithm.Run();
			running = false;

			if (algorithm.Solved)
			{
				// From first move to last, because the stack has the last move at the top (index 0) of the stack.
				solutionHops = algorithm.UndoStack.Reverse().ToList();
				RestoreBoard();
				solutionHops.Add(new Hop(-1, 0, 0));     // Fake last entry so we can step to the actual 1 peg remaining.
				lbSolution.DataSource = solutionHops;
				// ShowPegs();
				solutionStep = 0;
				ckShowUi.Checked = true;

				// Reset board so we can navigate the solution
				board.FillAllCells();
				board.RemovePeg(startPosition);
			}
			else
			{
				MessageBox.Show("No Solution!", "Brain Teaser", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
		}

		/// <summary>
		/// Undo all hops to restore board (the cell's peg colors) to their original state.
		/// </summary>
		protected void RestoreBoard()
		{
			while (algorithm.UndoStack.Count > 0)
			{
				Hop hop = algorithm.UndoStack.Pop();
				board.UndoHopPeg(hop);
				Application.DoEvents();
			}
		}

		protected void OnSolutionStepChanged(object sender, EventArgs e)
		{
			int newSolStep = lbSolution.SelectedIndex;

			while (newSolStep > solutionStep)
			{
				// Move forward through solution steps.
				board.HopPeg(solutionHops[solutionStep++]);
			}

			while (newSolStep < solutionStep)
			{
				board.UndoHopPeg(solutionHops[solutionStep-1]);
				--solutionStep;
			}
		}

		private void btnSingleStep_Click(object sender, EventArgs e)
		{
			// Force UI as otherwise single stepping is sort of pointless.
			ckShowUi.Checked = true;

			if (!singleStepping)
			{
				ShowPegs();
				algorithm.Initialize(startPosition);
				algorithm.StartStepper();		// Used for yield recursion.
			}

			bool next = algorithm.Step();

			if (next)
			{
				algorithm.PushHop();
				lbSolution.DataSource = algorithm.UndoStack.Reverse().ToList();
			}

			singleStepping = next || (board.RemainingPegs > 1 && next);
		}

		private void OnStartPositionChanged(object sender, EventArgs e)
		{
			startPosition = (int)nudStartPosition.Value;
			ShowPegs();
		}
	}
}
