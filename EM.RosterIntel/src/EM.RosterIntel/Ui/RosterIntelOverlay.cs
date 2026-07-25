using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using EM.RosterIntel.Data;
using EM.RosterIntel.Scoring;
using EM.RosterIntel.Util;
using UnityEngine;

namespace EM.RosterIntel.Ui;

public sealed class RosterIntelOverlay : MonoBehaviour
{
	public RosterIntelOverlay(IntPtr ptr)
		: base(ptr)
	{
	}

	private ManualLogSource? _log;

	private ModConfig? _config;

	private IRosterDataProvider? _provider;

	private RosterScoringEngine? _scorer;

	private bool _visible = true;

	private Rect _windowRect;

	private RosterReport? _cachedReport;

	private float _nextRefreshTime;

	private int _activeTab;

	private bool _draggingWindow;

	private Vector2 _dragOffset;

	private bool _minimized;

	private float _expandedHeight;

	private const int TabRoster = 0;

	private const int TabTransfers = 1;

	private const int TabDetails = 2;

	private const int TabHelp = 3;

	private GUIStyle? _panelFillStyle;

	private GUIStyle? _panelHeaderStyle;

	private GUIStyle? _panelEdgeStyle;

	private Texture2D? _panelBackgroundTexture;

	private Texture2D? _panelHeaderTexture;

	private Texture2D? _panelEdgeTexture;

	private Color _lastPanelColor = new Color(-1f, -1f, -1f, -1f);

	private bool _chromeDrawingFailed;

	private bool _contentDrawingFailureLogged;

	private const float Pad = 16f;

	private const float Line = 20f;

	private const float SmallLine = 18f;

	public void Initialize()
	{
		_log = OverlayServices.Log;
		_config = OverlayServices.Config;
		_provider = OverlayServices.Provider;
		_scorer = OverlayServices.Scorer;
		if (_config != null)
		{
			float num = _config.PanelWidth.Value;
			float num2 = _config.PanelHeight.Value;
			if (_config.AutoFitPanel.Value)
			{
				num = Math.Max(num, _config.MinPanelWidth.Value);
				num2 = Math.Max(num2, _config.MinPanelHeight.Value);
			}
			_windowRect = new Rect(_config.PanelX.Value, _config.PanelY.Value, num, num2);
			_expandedHeight = num2;
			KeepWindowOnScreen();
		}
		RefreshReport(forceLog: false);
	}

	private void Update()
	{
		if (_config == null)
		{
			return;
		}
		if (Input.GetKeyDown(_config.ToggleKeyCode))
		{
			_visible = !_visible;
			if (_visible)
			{
				RefreshReport(forceLog: false);
			}
		}
		if (Input.GetKeyDown(_config.ResizeGrowKeyCode))
		{
			ResizePanel(1);
		}
		if (Input.GetKeyDown(_config.ResizeShrinkKeyCode))
		{
			ResizePanel(-1);
		}
		if (Input.GetKeyDown(_config.ResetWindowKeyCode))
		{
			ResetWindow();
		}
		if (Time.unscaledTime >= _nextRefreshTime)
		{
			_nextRefreshTime = Time.unscaledTime + Math.Max(1f, _config.RefreshIntervalSeconds.Value);
			RefreshReport(forceLog: false);
		}
	}

	private void OnGUI()
	{
		if (_config == null || _config.LogOnlyMode.Value || !_visible)
		{
			return;
		}
		try
		{
			float num = Math.Max(0.5f, _config.Scale.Value);
			Matrix4x4 matrix = GUI.matrix;
			Color color = GUI.color;
			Color backgroundColor = GUI.backgroundColor;
			GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(num, num, 1f));
			float num2 = (_config.ForceOpaquePanel.Value ? 1f : Math.Clamp(_config.PanelOpacity.Value, 0.8f, 1f));
			GUI.backgroundColor = new Color(0f, 0f, 0f, num2);
			GUI.color = Color.white;
			HandleGlobalWindowDrag(num);
			KeepWindowOnScreen();
			DrawWindowWithFallback(_windowRect, string.Empty);
			GUI.matrix = matrix;
			GUI.color = color;
			GUI.backgroundColor = backgroundColor;
		}
		catch (Exception ex)
		{
			_visible = false;
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)"RosterIntel release safe IMGUI overlay failed; hiding overlay for this session.");
			}
			ManualLogSource? log2 = _log;
			if (log2 != null)
			{
				log2.LogWarning((object)ex.ToString());
			}
		}
	}

	private Rect DrawWindowWithFallback(Rect rect, string title)
	{
		return GUI.Window(778260, rect, WindowFunction.op_Implicit((Action<int>)DrawSafeWindow), title);
	}

	private void DrawSafeWindow(int id)
	{
		try
		{
			DrawWindowContent(id);
		}
		catch (Exception ex)
		{
			if (!_contentDrawingFailureLogged)
			{
				_contentDrawingFailureLogged = true;
				ManualLogSource? log = _log;
				if (log != null)
				{
					log.LogWarning((object)"RosterIntel release window content failed once; drawing compact fallback instead of spamming the log.");
				}
				ManualLogSource? log2 = _log;
				if (log2 != null)
				{
					log2.LogWarning((object)ex.ToString());
				}
			}
			GUI.Label(new Rect(16f, 34f, Math.Max(260f, ((Rect)(ref _windowRect)).width - 32f), 20f), "EM Roster Intel Release: UI fallback active. Пришли новый LogOutput.log.");
		}
	}

	private void DrawWindowContent(int id)
	{
		DrawPanelChrome();
		float y = 6f;
		float num = ((Rect)(ref _windowRect)).width - 32f;
		Header(ref y, num);
		if (_minimized)
		{
			return;
		}
		DrawTabs(ref y, num);
		if (_cachedReport == null)
		{
			DrawSectionTitle(ref y, num, "СТАТУС", new Color(0.16f, 0.55f, 0.9f, 1f));
			Text(16f, y, num, "Нет отчёта по составу", Warn());
			y += 20f;
			Text(16f, y, num, "Открой экран «Состав» и подожди 3–5 секунд.", White());
			y += 20f;
			return;
		}
		if (_cachedReport.StarterCount <= 0 || _cachedReport.Players.Count == 0)
		{
			DrawSectionTitle(ref y, num, "LIVE-СОСТАВ", Warn());
			Text(16f, y, num, "Live-состав ещё не захвачен.", Warn());
			y += 20f;
			Text(16f, y, num, "Открой экран «Состав» и подожди, пока появится ОСНОВА 5/5.", White());
			y += 20f;
			Text(16f, y, num, "Sample не показывается по умолчанию, чтобы не путать реальные числа.", Muted());
			y += 20f;
			return;
		}
		switch (_activeTab)
		{
		case 1:
			DrawTransfersTab(ref y, num);
			break;
		case 2:
			DrawDetailsTab(ref y, num);
			break;
		case 3:
			DrawHelpTab(ref y, num);
			break;
		default:
			DrawRosterTab(ref y, num);
			break;
		}
	}

	private void DrawTabs(ref float y, float w)
	{
		float num = 16f;
		float num2 = 24f;
		float num3 = 6f;
		float num4 = Math.Min(150f, Math.Max(112f, (w - num3 * 3f) / 4f));
		DrawTabButton(new Rect(num, y, num4, num2), 0, "СОСТАВ");
		DrawTabButton(new Rect(num + (num4 + num3), y, num4, num2), 1, "ТРАНСФЕРЫ");
		DrawTabButton(new Rect(num + (num4 + num3) * 2f, y, num4, num2), 2, "ДЕТАЛИ");
		DrawTabButton(new Rect(num + (num4 + num3) * 3f, y, num4, num2), 3, "СПРАВКА");
		y += num2 + 8f;
	}

	private void DrawTabButton(Rect rect, int tab, string title)
	{
		bool flag = _activeTab == tab;
		if (_panelEdgeStyle != null)
		{
			Color color = (flag ? new Color(0.04f, 0.34f, 0.38f, 1f) : new Color(0.025f, 0.06f, 0.08f, 1f));
			DrawStyledBox(rect, color, _panelEdgeStyle);
			DrawStyledBox(new Rect(((Rect)(ref rect)).x, ((Rect)(ref rect)).y + ((Rect)(ref rect)).height - 2f, ((Rect)(ref rect)).width, 2f), (Color)(flag ? Cyan() : new Color(0.1f, 0.22f, 0.28f, 1f)), _panelEdgeStyle);
		}
		Text(((Rect)(ref rect)).x + 10f, ((Rect)(ref rect)).y + 2f, Math.Max(20f, ((Rect)(ref rect)).width - 20f), flag ? ("• " + title) : title, flag ? Cyan() : Muted());
		if (ConsumeLeftClick(rect))
		{
			_activeTab = tab;
		}
	}

	private void DrawRosterTab(ref float y, float w)
	{
		DrawSectionTitle(ref y, w, "КОМАНДНЫЙ ПРОФИЛЬ", new Color(0.2f, 0.75f, 0.55f, 1f));
		DrawMetric(ref y, "Фит состава", _cachedReport.TeamFit);
		DrawMetric(ref y, "Огневая мощь", _cachedReport.Firepower);
		DrawMetric(ref y, "Вклад IGL", _cachedReport.IGL);
		DrawMetric(ref y, "AWP", _cachedReport.AWP);
		DrawMetric(ref y, "Тактика/утилити", _cachedReport.Utility);
		DrawMetric(ref y, "Форма/статы", _cachedReport.Performance);
		y += 4f;
		DrawActionSection(ref y, w);
		ModConfig? config = _config;
		if (config != null && config.ShowPlayerRows.Value && _cachedReport.Players.Count > 0)
		{
			DrawStarterRows(ref y, w);
			DrawBenchRows(ref y, w);
		}
		DrawFooter(ref y, w);
	}

	private void DrawTransfersTab(ref float y, float w)
	{
		DrawTransferRadarSection(ref y, w);
	}

	private void DrawDetailsTab(ref float y, float w)
	{
		RosterReport cachedReport = _cachedReport;
		DrawStatusSection(ref y, w);
		DrawSectionTitle(ref y, w, "ДЕТАЛИ ОБЪЕКТИВНОСТИ", new Color(0.35f, 0.7f, 1f, 1f));
		Text(16f, y, w, BuildAuditLine(cachedReport), ObjectiveConfidenceColor(cachedReport.ObjectiveConfidence));
		y += 18f;
		Text(16f, y, w, BuildPrecisionChecklistLine(cachedReport), PrecisionChecklistColor(cachedReport));
		y += 18f;
		Text(16f, y, w, BuildPlayerEvidenceLine(cachedReport), EvidenceSourcesColor(cachedReport));
		y += 18f;
		Text(16f, y, w, BuildEvidenceSources(cachedReport), EvidenceSourcesColor(cachedReport));
		y += 18f;
		Text(16f, y, w, BuildMatchHistoryLine(cachedReport), MatchHistoryColor(cachedReport));
		y += 18f;
		if (!string.IsNullOrWhiteSpace(cachedReport.RecommendationAudit))
		{
			Text(16f, y, w, "Аудит рекомендации: " + TranslateAudit(cachedReport.RecommendationAudit), Muted());
			y += 18f;
		}
		DrawSectionTitle(ref y, w, "ИСТОЧНИКИ ДАННЫХ", new Color(0.35f, 0.7f, 1f, 1f));
		Text(16f, y, w, "T = матчи/tier-log: история карт, rating и K/D из матчей против уровней top1/top5/top10/...", Muted());
		y += 18f;
		Text(16f, y, w, "D = direct/profile: данные напрямую из карточки/профиля игрока, если UI их отдал.", Muted());
		y += 18f;
		Text(16f, y, w, "A = attributes: только видимые скиллы и роль. Полезно, но слабее статистики.", Muted());
		y += 18f;
	}

	private void DrawHelpTab(ref float y, float w)
	{
		DrawSectionTitle(ref y, w, "УПРАВЛЕНИЕ", Cyan());
		Text(16f, y, w, "Перемещение: зажми пустую верхнюю полосу окна и тяни мышью.", Muted());
		y += 18f;
		Text(16f, y, w, "Кнопки - / + меняют размер. _ сворачивает, □ разворачивает, × скрывает окно.", Muted());
		y += 18f;
		Text(16f, y, w, "F9 возвращает скрытое окно. Backspace сбрасывает позицию и размер.", Muted());
		y += 18f;
		DrawSectionTitle(ref y, w, "РОЛИ И ИДЕИ", new Color(0.55f, 0.8f, 1f, 1f));
		Text(16f, y, w, "IGL — план/структура. AWPer — снайпер. Rifler — огонь. Support — utility. Lurker — давление/клатчи.", Muted());
		y += 18f;
		Text(16f, y, w, "role-safe / та же роль: кандидат меняет игрока без перестройки состава.", Muted());
		y += 18f;
		Text(16f, y, w, "смена роли: кандидат силён, но нужно менять позиции/обязанности, поэтому риск выше.", Muted());
		y += 18f;
		Text(16f, y, w, "luxury AWP / лишний AWP: сильный снайпер, но текущий AWP уже закрывает роль.", Muted());
		y += 18f;
		Text(16f, y, w, "structure shift / перестройка: трансфер меняет IGL, структуру или стиль команды.", Muted());
		y += 18f;
		DrawSectionTitle(ref y, w, "БЕЗОПАСНОСТЬ", Good());
		Text(16f, y, w, "Мод read-only: не пишет сейвы, не покупает игроков, не меняет составы и не патчит симуляцию матчей.", Good());
		y += 18f;
		DrawFooter(ref y, w);
	}

	private void HandleGlobalWindowDrag(float scale)
	{
		if (_config == null)
		{
			return;
		}
		try
		{
			Vector2 val = default(Vector2);
			((Vector2)(ref val))._002Ector(Input.mousePosition.x / scale, ((float)Screen.height - Input.mousePosition.y) / scale);
			Rect val2 = default(Rect);
			((Rect)(ref val2))._002Ector(((Rect)(ref _windowRect)).x + 4f, ((Rect)(ref _windowRect)).y + 3f, Math.Max(40f, ((Rect)(ref _windowRect)).width - 142f), 28f);
			if (Input.GetMouseButtonDown(0) && ((Rect)(ref val2)).Contains(val))
			{
				_draggingWindow = true;
				_dragOffset = val - new Vector2(((Rect)(ref _windowRect)).x, ((Rect)(ref _windowRect)).y);
			}
			if (_draggingWindow && Input.GetMouseButton(0))
			{
				((Rect)(ref _windowRect)).x = val.x - _dragOffset.x;
				((Rect)(ref _windowRect)).y = val.y - _dragOffset.y;
			}
			if (Input.GetMouseButtonUp(0))
			{
				_draggingWindow = false;
				PersistWindowGeometry();
			}
		}
		catch
		{
			_draggingWindow = false;
		}
	}

	private bool ConsumeLeftClick(Rect rect)
	{
		Event current = Event.current;
		if (current == null || (int)current.type != 0 || current.button != 0 || !((Rect)(ref rect)).Contains(current.mousePosition))
		{
			return false;
		}
		current.Use();
		return true;
	}

	private void DrawTopButton(Rect rect, string label, int actionCode, Color accent)
	{
		if (_panelEdgeStyle != null)
		{
			DrawStyledBox(rect, new Color(0.035f, 0.075f, 0.095f, 1f), _panelEdgeStyle);
			DrawStyledBox(new Rect(((Rect)(ref rect)).x, ((Rect)(ref rect)).y + ((Rect)(ref rect)).height - 1f, ((Rect)(ref rect)).width, 1f), new Color(accent.r, accent.g, accent.b, 0.7f), _panelEdgeStyle);
		}
		Text(((Rect)(ref rect)).x + 8f, ((Rect)(ref rect)).y + 1f, Math.Max(12f, ((Rect)(ref rect)).width - 10f), label, accent);
		if (ConsumeLeftClick(rect))
		{
			switch (actionCode)
			{
			case 1:
				ResizePanel(-1);
				break;
			case 2:
				ResizePanel(1);
				break;
			case 3:
				ToggleMinimized();
				break;
			case 4:
				_visible = false;
				break;
			}
		}
	}

	private void ToggleMinimized()
	{
		if (_config != null)
		{
			if (_minimized)
			{
				_minimized = false;
				float value = ((_expandedHeight > 100f) ? _expandedHeight : _config.PanelHeight.Value);
				((Rect)(ref _windowRect)).height = Math.Clamp(value, _config.MinPanelHeight.Value, _config.MaxPanelHeight.Value);
			}
			else
			{
				_expandedHeight = Math.Max(((Rect)(ref _windowRect)).height, _config.MinPanelHeight.Value);
				_minimized = true;
				((Rect)(ref _windowRect)).height = 42f;
			}
			KeepWindowOnScreen();
		}
	}

	private void PersistWindowGeometry()
	{
		if (_config != null)
		{
			_config.PanelX.Value = ((Rect)(ref _windowRect)).x;
			_config.PanelY.Value = ((Rect)(ref _windowRect)).y;
			_config.PanelWidth.Value = ((Rect)(ref _windowRect)).width;
			if (!_minimized)
			{
				_config.PanelHeight.Value = ((Rect)(ref _windowRect)).height;
				_expandedHeight = ((Rect)(ref _windowRect)).height;
			}
		}
	}

	private void DrawPanelChrome()
	{
		ModConfig? config = _config;
		if (config == null || !config.ForceOpaquePanel.Value || _chromeDrawingFailed)
		{
			return;
		}
		try
		{
			EnsureOpaqueStyles();
			if (_panelFillStyle != null)
			{
				DrawStyledBox(new Rect(0f, 0f, ((Rect)(ref _windowRect)).width, ((Rect)(ref _windowRect)).height), PanelBackgroundColor(), _panelFillStyle);
			}
			if (_panelEdgeStyle != null)
			{
				Color color = default(Color);
				((Color)(ref color))._002Ector(0.16f, 0.36f, 0.42f, 1f);
				DrawStyledBox(new Rect(0f, 0f, ((Rect)(ref _windowRect)).width, 2f), color, _panelEdgeStyle);
				DrawStyledBox(new Rect(0f, ((Rect)(ref _windowRect)).height - 2f, ((Rect)(ref _windowRect)).width, 2f), color, _panelEdgeStyle);
				DrawStyledBox(new Rect(0f, 0f, 2f, ((Rect)(ref _windowRect)).height), color, _panelEdgeStyle);
				DrawStyledBox(new Rect(((Rect)(ref _windowRect)).width - 2f, 0f, 2f, ((Rect)(ref _windowRect)).height), color, _panelEdgeStyle);
			}
			if (_panelHeaderStyle != null)
			{
				DrawStyledBox(new Rect(2f, 2f, Math.Max(0f, ((Rect)(ref _windowRect)).width - 4f), 28f), new Color(0.025f, 0.04f, 0.055f, 1f), _panelHeaderStyle);
			}
		}
		catch (Exception ex)
		{
			_chromeDrawingFailed = true;
			ManualLogSource? log = _log;
			if (log != null)
			{
				log.LogWarning((object)"RosterIntel release opaque chrome drawing failed once; disabling chrome fill to avoid log spam. Window/style fallback remains active.");
			}
			ManualLogSource? log2 = _log;
			if (log2 != null)
			{
				log2.LogWarning((object)(ex.GetType().Name + ": " + ex.Message));
			}
		}
	}

	private void EnsureOpaqueStyles()
	{
		Color val = PanelBackgroundColor();
		if (_panelFillStyle == null || !SameColor(_lastPanelColor, val))
		{
			_lastPanelColor = val;
			_panelBackgroundTexture = Texture2D.whiteTexture;
			_panelHeaderTexture = Texture2D.whiteTexture;
			_panelEdgeTexture = Texture2D.whiteTexture;
			_panelFillStyle = CreateSolidBoxStyle(_panelBackgroundTexture);
			_panelHeaderStyle = CreateSolidBoxStyle(_panelHeaderTexture);
			_panelEdgeStyle = CreateSolidBoxStyle(_panelEdgeTexture);
		}
	}

	private static GUIStyle CreateSolidBoxStyle(Texture2D texture)
	{
		GUIStyle val = new GUIStyle();
		ApplyBackgroundToAllStates(val, texture, Color.clear);
		val.padding = new RectOffset(0, 0, 0, 0);
		val.margin = new RectOffset(0, 0, 0, 0);
		val.border = new RectOffset(0, 0, 0, 0);
		return val;
	}

	private static void DrawStyledBox(Rect rect, Color color, GUIStyle style)
	{
		Color color2 = GUI.color;
		try
		{
			GUI.color = color;
			GUI.Box(rect, string.Empty, style);
		}
		finally
		{
			GUI.color = color2;
		}
	}

	private static void ApplyBackgroundToAllStates(GUIStyle style, Texture2D texture, Color textColor)
	{
		style.normal.background = texture;
		style.hover.background = texture;
		style.active.background = texture;
		style.focused.background = texture;
		style.onNormal.background = texture;
		style.onHover.background = texture;
		style.onActive.background = texture;
		style.onFocused.background = texture;
		style.normal.textColor = textColor;
		style.hover.textColor = textColor;
		style.active.textColor = textColor;
		style.focused.textColor = textColor;
		style.onNormal.textColor = textColor;
		style.onHover.textColor = textColor;
		style.onActive.textColor = textColor;
		style.onFocused.textColor = textColor;
	}

	private static bool SameColor(Color a, Color b)
	{
		if (Math.Abs(a.r - b.r) < 0.001f && Math.Abs(a.g - b.g) < 0.001f && Math.Abs(a.b - b.b) < 0.001f)
		{
			return Math.Abs(a.a - b.a) < 0.001f;
		}
		return false;
	}

	private Color PanelBackgroundColor()
	{
		if (_config == null)
		{
			return new Color(0.015f, 0.018f, 0.026f, 1f);
		}
		return new Color(Math.Clamp(_config.PanelBackgroundRed.Value, 0f, 1f), Math.Clamp(_config.PanelBackgroundGreen.Value, 0f, 1f), Math.Clamp(_config.PanelBackgroundBlue.Value, 0f, 1f), 1f);
	}

	private void Header(ref float y, float w)
	{
		Text(16f, y, Math.Max(240f, w - 150f), "АНАЛИТИКА СОСТАВА · RELEASE", Cyan());
		float num = 5f;
		DrawTopButton(new Rect(((Rect)(ref _windowRect)).width - 132f, num, 26f, 22f), "-", 1, Muted());
		DrawTopButton(new Rect(((Rect)(ref _windowRect)).width - 102f, num, 26f, 22f), "+", 2, Cyan());
		DrawTopButton(new Rect(((Rect)(ref _windowRect)).width - 72f, num, 26f, 22f), _minimized ? "□" : "_", 3, Average());
		DrawTopButton(new Rect(((Rect)(ref _windowRect)).width - 42f, num, 30f, 22f), "×", 4, Warn());
		y += 28f;
	}

	private void DrawSectionTitle(ref float y, float w, string title, Color accent)
	{
		if (_panelEdgeStyle != null)
		{
			DrawStyledBox(new Rect(12f, y - 1f, Math.Max(0f, w + 8f), 19f), new Color(0.025f, 0.04f, 0.052f, 0.78f), _panelEdgeStyle);
			DrawStyledBox(new Rect(16f, y + 2f, 5f, 14f), accent, _panelEdgeStyle);
			DrawStyledBox(new Rect(24f, y + 20f - 2f, Math.Max(0f, w - 8f), 1f), new Color(accent.r, accent.g, accent.b, 0.45f), _panelEdgeStyle);
		}
		Text(30f, y, Math.Max(0f, w - 14f), title, accent);
		y += 24f;
	}

	private void DrawStatusSection(ref float y, float w)
	{
		if (_cachedReport != null)
		{
			RosterReport cachedReport = _cachedReport;
			DrawSectionTitle(ref y, w, "СТАТУС ОТЧЁТА", cachedReport.IsManagedTeamFocus ? Good() : Average());
			string value = cachedReport.ObjectiveConfidence.ToString("0") + "%";
			string value2 = (cachedReport.IsManagedTeamFocus ? "моя команда" : "scouting");
			string value3 = (cachedReport.IsManagedTeamFocus ? "фокус команды 100%" : "фокус scouting");
			string value4 = $"состав {cachedReport.StarterCount}/5";
			string value5 = $"статы {Math.Round(cachedReport.StatsCoverage * (double)Math.Max(1, cachedReport.StarterCount) / 100.0):0}/{Math.Max(1, cachedReport.StarterCount)}";
			Text(16f, y, w, $"Команда: {cachedReport.TeamName}  |  {value2}  |  {value3}  |  объективность аналитики {value}  |  {value4}  |  {value5}", ObjectiveConfidenceColor(cachedReport.ObjectiveConfidence));
			y += 20f;
			Text(16f, y, w, $"Источники: direct {cachedReport.DirectStatsPlayers}/{Math.Max(1, cachedReport.StarterCount)} · tier/log {cachedReport.LogStatsPlayers}/{Math.Max(1, cachedReport.StarterCount)} · attrs-only {cachedReport.AttributeOnlyPlayers}/{Math.Max(1, cachedReport.StarterCount)} · история карт {cachedReport.TotalTierMaps}", EvidenceSourcesColor(cachedReport));
			y += 20f;
			Text(16f, y, w, "История: " + BuildMatchHistoryText(cachedReport), MatchHistoryColor(cachedReport));
			y += 20f;
			Text(16f, y, w, BuildCompactChecklistLine(cachedReport), PrecisionChecklistColor(cachedReport));
			y += 20f;
			y += 4f;
		}
	}

	private void DrawActionSection(ref float y, float w)
	{
		if (_cachedReport == null)
		{
			return;
		}
		DrawSectionTitle(ref y, w, "ВЕРДИКТ И ПЛАН", new Color(0.85f, 0.72f, 0.25f, 1f));
		Text(16f, y, w, "Вердикт: " + TranslateVerdict(_cachedReport.Verdict), Good());
		y += 20f;
		Text(16f, y, w, "План проверки: " + BuildNextStepLine(_cachedReport), Average());
		y += 20f;
		Text(16f, y, w, "Зона внимания: " + SafeName(_cachedReport.WeakestLink), White());
		y += 20f;
		Text(16f, y, w, "Замена с бенча: " + TranslateSwap(_cachedReport.BestSwap), White());
		y += 20f;
		for (int i = 0; i < 2; i++)
		{
			string text = DetailedVerdictLine(i);
			if (!string.IsNullOrWhiteSpace(text))
			{
				Text(16f, y, w, text, Good());
				y += 18f;
			}
		}
		y += 5f;
	}

	private void DrawVerdictBlock(ref float y, float w)
	{
		if (_cachedReport == null)
		{
			return;
		}
		Text(16f, y, w, "Вердикт: " + TranslateVerdict(_cachedReport.Verdict), Good());
		y += 20f;
		for (int i = 0; i < 3; i++)
		{
			string text = DetailedVerdictLine(i);
			if (!string.IsNullOrWhiteSpace(text))
			{
				Text(16f, y, w, text, Good());
				y += 18f;
			}
		}
		y += 5f;
	}

	private string DetailedVerdictLine(int index)
	{
		RosterReport cachedReport = _cachedReport;
		if (cachedReport == null)
		{
			return string.Empty;
		}
		PlayerScores playerScores = cachedReport.Players.FirstOrDefault((PlayerScores p) => string.Equals(p.Role, "AWPer", StringComparison.OrdinalIgnoreCase));
		int num = 0;
		if (playerScores != null && playerScores.AWP >= 17.5 && playerScores.RoleAdjusted >= 17.0)
		{
			num = 1;
		}
		else if (cachedReport.IGL >= 17.0 && cachedReport.Firepower < 16.0)
		{
			num = 2;
		}
		else if (cachedReport.TeamFit >= 18.0)
		{
			num = 3;
		}
		switch (num)
		{
		case 1:
			switch (index)
			{
			case 0:
				return "AWP-роль закрыта: не трогай снайпера без явного superstar-апгрейда.";
			case 1:
				return "Если команда выглядит сильной — сыграй минимум 5 прак-карт или полноценный турнир перед трансфером.";
			}
			break;
		case 2:
			switch (index)
			{
			case 0:
				return "IGL даёт структуру; проверяй второго фрагера минимум на 5 прак-картах или в турнире.";
			case 1:
				return "Меняй только того, кто стабильно проседает по роли, а не капитана за низкий личный урон.";
			}
			break;
		case 3:
			switch (index)
			{
			case 0:
				return "Ростер уже сильный: сначала проверь его на 5 прак-картах или в полноценном турнире.";
			case 1:
				return "Небольшой численный прирост не причина ломать рабочие роли и химию состава.";
			}
			break;
		}
		return index switch
		{
			0 => "Состав рабочий: нужен контрольный блок — минимум 5 прак-карт или полный турнир.", 
			1 => "Апгрейд должен быть очевидным по роли, статистике и team-fit, иначе лучше оставить основу.", 
			_ => string.Empty, 
		};
	}

	private void DrawTransferRadarSection(ref float y, float w)
	{
		if (_cachedReport == null)
		{
			return;
		}
		RosterReport cachedReport = _cachedReport;
		if (string.IsNullOrWhiteSpace(cachedReport.TransferRadarStatus) || cachedReport.TransferRadarStatus == "off")
		{
			DrawSectionTitle(ref y, w, "ТРАНСФЕРЫ", new Color(0.75f, 0.45f, 1f, 1f));
			Text(16f, y, w, "Радар выключен или ещё нет кандидатов. Открой 2–3 чужие команды и вернись на свою.", Muted());
			y += 18f;
			return;
		}
		DrawSectionTitle(ref y, w, "ТРАНСФЕРЫ · SPORT-ONLY", new Color(0.75f, 0.45f, 1f, 1f));
		string text = BuildTransferCompactStatus(cachedReport);
		Text(16f, y, w, text, Muted());
		y += 20f;
		List<TransferRadarEntry> list = cachedReport.TransferRadar.Take(4).ToList();
		if (list.Count <= 0)
		{
			Text(16f, y, w, "Пока нет подходящих кандидатов. Открой составы соперников/скаутинг, затем вернись на свою команду.", Muted());
			y += 20f;
			return;
		}
		float num = 16f;
		float num2 = 76f;
		float num3 = 142f;
		float num4 = 82f;
		float num5 = 318f;
		float num6 = 142f;
		float width = Math.Max(250f, w - (num2 + num3 + num4 + num5 + num6) - 8f);
		Text(num, y, num2, "Статус", Muted());
		Text(num + num2, y, num3, "Кандидат", Muted());
		Text(num + num2 + num3, y, num4, "Вместо", Muted());
		Text(num + num2 + num3 + num4, y, num5, "Почему подходит", Muted());
		Text(num + num2 + num3 + num4 + num5, y, num6, "Данные", Muted());
		Text(num + num2 + num3 + num4 + num5 + num6, y, width, "Действие", Muted());
		y += 18f;
		int num7 = 0;
		foreach (TransferRadarEntry item in list)
		{
			Color color = ((item.Tier == "priority") ? Good() : ((item.Tier == "trial") ? Average() : Muted()));
			if (_panelEdgeStyle != null)
			{
				Color color2 = ((num7 % 2 == 0) ? new Color(1f, 1f, 1f, 0.03f) : new Color(1f, 1f, 1f, 0.014f));
				DrawStyledBox(new Rect(12f, y - 1f, Math.Max(0f, w + 8f), 18f), color2, _panelEdgeStyle);
			}
			string text2 = TransferTierReadable(item.Tier);
			string text3 = Shorten(item.CandidateNick + " (" + ShortTeam(item.CandidateTeam) + ")", 21);
			string text4 = Shorten(item.ReplaceNick, 11);
			string text5 = Shorten(TransferIdeaReadable(item.Profile, item.Lane, item.Risk), 52);
			string text6 = Shorten(ReadableEvidence(item.Evidence), 22);
			string text7 = Shorten(ReadableAction(item.Action, item.Lane), 38);
			Text(num, y, num2, text2, color);
			Text(num + num2, y, num3, text3, color);
			Text(num + num2 + num3, y, num4, text4, White());
			Text(num + num2 + num3 + num4, y, num5, text5, White());
			Text(num + num2 + num3 + num4 + num5, y, num6, text6, EvidenceReadableColor(item.Evidence));
			Text(num + num2 + num3 + num4 + num5 + num6, y, width, text7, color);
			y += 18f;
			num7++;
		}
		y += 4f;
	}

	private static string BuildTransferCompactStatus(RosterReport r)
	{
		int num = ExtractPoolCount(r.TransferRadarStatus);
		int count = r.TransferRadar.Count;
		string value = ((num > 0) ? $"найдено кандидатов: {num}" : "кандидаты: ?");
		return $"{value} · показано {count} · режим: sport-only shortlist";
	}

	private static string TransferTierReadable(string tier)
	{
		if (!(tier == "priority"))
		{
			if (tier == "trial")
			{
				return "тест";
			}
			return "следить";
		}
		return "брать";
	}

	private static string TransferIdeaReadable(string profile, string lane, string risk)
	{
		return lane switch
		{
			"luxury-awp" => "сильный AWP, но эта роль уже закрыта", 
			"structure-shift" => "усилит план/IGL, но изменит структуру", 
			"role-change" => "сильнее, но потребует сменить роли", 
			_ => profile switch
			{
				"AWP target" => "прямой апгрейд основного снайпера", 
				"IGL/structure target" => "больше структуры, плана и лидерства", 
				"star-rifler target" => "больше rifle-урона и star-impact", 
				"system/utility target" => "больше пользы через utility и позиции", 
				"clutch target" => "сильнее в клатчах и поздних раундах", 
				"rifle upgrade watch" => "усиление rifle-роли в основе", 
				_ => "прямая замена без перестройки ролей", 
			}, 
		};
	}

	private static string ReadableEvidence(string evidence)
	{
		if (string.IsNullOrWhiteSpace(evidence))
		{
			return "данных мало";
		}
		string text = (evidence.Contains("D+T") ? "D+T" : (evidence.Contains("T") ? "T" : (evidence.Contains("D") ? "D" : "A")));
		int num = ExtractNumberAfter(evidence, "maps ");
		double num2 = ExtractRating(evidence);
		int num3 = ExtractNumberAfter(evidence, "signals ");
		if (text == "D+T" && num > 0 && num2 > 0.0)
		{
			return $"{num} match · профиль · r{num2:0.00}";
		}
		if (text == "T" && num > 0 && num2 > 0.0)
		{
			return $"{num} match · r{num2:0.00}";
		}
		if (text == "T" && num > 0)
		{
			return $"{num} match";
		}
		if (text == "D" && num3 > 0)
		{
			return $"карточка · {num3} сигн.";
		}
		if (text == "D")
		{
			return "карточка игрока";
		}
		if (text == "A")
		{
			return "только скиллы";
		}
		return text;
	}

	private static string ReadableAction(string action, string lane)
	{
		return lane switch
		{
			"luxury-awp" => "AWP закрыт — только наблюдать", 
			"structure-shift" => "только если хочешь перестройку", 
			"role-change" => "сначала проверь новую роль", 
			_ => action switch
			{
				"sport priority: compare in game" => "сравнить в игре как апгрейд", 
				"scout + 5 prac maps" => "shortlist + 5 прак-карт", 
				"add to watchlist" => "оставить в watchlist", 
				"watch-only: AWP role closed" => "AWP закрыт — наблюдать", 
				"plan restructure" => "вариант под перестройку", 
				"scout structure fit" => "проверить структуру", 
				"scout role-change first" => "проверить смену роли", 
				_ => string.IsNullOrWhiteSpace(action) ? "проверить вручную" : action, 
			}, 
		};
	}

	private static Color EvidenceReadableColor(string evidence)
	{
		if (string.IsNullOrWhiteSpace(evidence))
		{
			return Muted();
		}
		if (evidence.Contains("D+T"))
		{
			return Elite();
		}
		if (evidence.Contains("T"))
		{
			return Good();
		}
		if (evidence.Contains("D"))
		{
			return Solid();
		}
		return Average();
	}

	private static int ExtractPoolCount(string status)
	{
		if (string.IsNullOrWhiteSpace(status))
		{
			return 0;
		}
		if (!int.TryParse(new string(status.SkipWhile((char ch) => !char.IsDigit(ch)).TakeWhile(char.IsDigit).ToArray()), out var result))
		{
			return 0;
		}
		return result;
	}

	private static string TransferTierShort(string tier)
	{
		if (!(tier == "priority"))
		{
			if (tier == "trial")
			{
				return "тест";
			}
			return "следить";
		}
		return "приоритет";
	}

	private static string TransferIdeaShort(string profile, string lane, string risk)
	{
		return lane switch
		{
			"luxury-awp" => "AWP уже закрыт", 
			"structure-shift" => "структура/IGL", 
			"role-change" => "нужна смена роли", 
			_ => profile switch
			{
				"AWP target" => "усиление AWP", 
				"IGL/structure target" => "план/структура", 
				"star-rifler target" => "star-rifler", 
				"system/utility target" => "роль+utility", 
				"clutch target" => "клатчи", 
				"rifle upgrade watch" => "rifle upgrade", 
				_ => "та же роль", 
			}, 
		};
	}

	private static string ShortEvidence(string evidence)
	{
		if (string.IsNullOrWhiteSpace(evidence))
		{
			return "?";
		}
		string text = (evidence.Contains("D+T") ? "D+T" : (evidence.Contains("T") ? "T" : (evidence.Contains("D") ? "D" : "A")));
		int num = ExtractNumberAfter(evidence, "maps ");
		double num2 = ExtractRating(evidence);
		if (num > 0 && num2 > 0.0)
		{
			return $"{text}: {num}к r{num2:0.00}";
		}
		if (num > 0)
		{
			return $"{text}: {num}к";
		}
		int num3 = ExtractNumberAfter(evidence, "signals ");
		if (num3 > 0)
		{
			return $"{text}: {num3}сигн";
		}
		if (!(text == "A"))
		{
			return text;
		}
		return "A: attrs";
	}

	private static string ShortAction(string action)
	{
		return action switch
		{
			"sport priority: compare in game" => "сравнить в игре; кандидат спортивно сильный", 
			"scout + 5 prac maps" => "добавить в shortlist и проверить 5 прак-карт", 
			"add to watchlist" => "оставить в watchlist", 
			"watch-only: AWP role closed" => "AWP закрыт — только следить", 
			"plan restructure" => "только при перестройке состава", 
			"scout structure fit" => "проверить, подходит ли под структуру", 
			"scout role-change first" => "сначала проверить смену роли", 
			_ => string.IsNullOrWhiteSpace(action) ? "проверить вручную" : action, 
		};
	}

	private static string ShortTeam(string team)
	{
		if (string.IsNullOrWhiteSpace(team))
		{
			return "?";
		}
		return team.Replace(" Esports", "").Replace(" Team", "").Replace("Natus Vincere", "NAVI")
			.Replace("Team Spirit", "Spirit")
			.Replace("Valtari", "Valtari")
			.Trim();
	}

	private static string Shorten(string text, int max)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= max)
		{
			return text ?? string.Empty;
		}
		return text.Substring(0, Math.Max(0, max - 1)) + "…";
	}

	private static int ExtractNumberAfter(string text, string marker)
	{
		int num = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return 0;
		}
		num += marker.Length;
		if (!int.TryParse(new string(text.Skip(num).TakeWhile(char.IsDigit).ToArray()), out var result))
		{
			return 0;
		}
		return result;
	}

	private static double ExtractRating(string text)
	{
		int num = text.IndexOf("rating ", StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return 0.0;
		}
		num += "rating ".Length;
		if (!double.TryParse(new string(text.Skip(num).TakeWhile((char ch) => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray()).Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
		{
			return 0.0;
		}
		return result;
	}

	private void DrawStarterRows(ref float y, float w)
	{
		List<PlayerScores> list = _cachedReport.Players.Take(5).ToList();
		int val = Math.Max(_cachedReport.StarterCount, list.Count);
		DrawSectionTitle(ref y, w, $"ОСНОВА ({list.Count}/{Math.Max(5, val)})", Cyan());
		Text(16f, y, 112f, "Игрок", Muted());
		Text(130f, y, 58f, "Роль", Muted());
		Text(190f, y, 34f, "Ev", Muted());
		Text(226f, y, 54f, "Фит", Muted());
		Text(282f, y, 62f, "Огонь", Muted());
		Text(346f, y, 62f, "Утил.", Muted());
		Text(410f, y, 52f, "IGL", Muted());
		Text(464f, y, Math.Max(190f, w - 448f), "Вывод", Muted());
		y += 18f;
		int val2 = Math.Max(0, (int)((((Rect)(ref _windowRect)).height - y - 92f) / 18f));
		int count = Math.Max(5, val2);
		int num = 0;
		foreach (PlayerScores item in list.Take(count))
		{
			if (_panelEdgeStyle != null && num % 2 == 0)
			{
				DrawStyledBox(new Rect(12f, y - 1f, Math.Max(0f, w + 8f), 18f), new Color(0.035f, 0.055f, 0.07f, 0.55f), _panelEdgeStyle);
			}
			num++;
			Text(16f, y, 112f, item.Nick, White());
			Text(130f, y, 58f, item.Role, RoleColor(item.Role));
			Text(190f, y, 34f, item.EvidenceTag, EvidenceTagColor(item.EvidenceTag));
			Text(226f, y, 54f, item.RoleAdjusted.ToString("0.0"), MetricColor(item.RoleAdjusted));
			Text(282f, y, 62f, item.Firepower.ToString("0.0"), MetricColor(item.Firepower));
			Text(346f, y, 62f, item.Utility.ToString("0.0"), MetricColor(item.Utility));
			Text(410f, y, 52f, item.IGLImpact.ToString("0.0"), MetricColor(item.IGLImpact));
			Text(464f, y, Math.Max(190f, w - 448f), TranslateVerdict(item.Verdict), MetricColor(item.RoleAdjusted));
			y += 18f;
		}
		if (list.Count < 5)
		{
			Text(16f, y, w, "Внимание: мод видит меньше 5 игроков основы. Открой «Состав» ещё раз или пришли лог.", Warn());
			y += 18f;
		}
		y += 6f;
	}

	private void DrawBenchRows(ref float y, float w)
	{
		if (_cachedReport == null || _cachedReport.BenchPlayers.Count == 0 || _config == null)
		{
			return;
		}
		int num = Math.Clamp(_config.MaxBenchRows.Value, 0, 5);
		if (num <= 0)
		{
			return;
		}
		int val = Math.Max(0, (int)((((Rect)(ref _windowRect)).height - y - 72f) / 18f));
		int num2 = Math.Min(num, Math.Min(val, _cachedReport.BenchPlayers.Count));
		if (num2 <= 0)
		{
			return;
		}
		DrawSectionTitle(ref y, w, $"ЗАПАС ({_cachedReport.BenchPlayers.Count})", new Color(0.35f, 0.7f, 1f, 1f));
		int num3 = 0;
		foreach (PlayerScores item in _cachedReport.BenchPlayers.Take(num2))
		{
			if (_panelEdgeStyle != null && num3 % 2 == 0)
			{
				DrawStyledBox(new Rect(12f, y - 1f, Math.Max(0f, w + 8f), 18f), new Color(0.03f, 0.045f, 0.065f, 0.45f), _panelEdgeStyle);
			}
			num3++;
			Text(16f, y, 112f, item.Nick, Muted());
			Text(130f, y, 58f, item.Role, RoleColor(item.Role));
			Text(190f, y, 34f, item.EvidenceTag, EvidenceTagColor(item.EvidenceTag));
			Text(226f, y, 54f, item.RoleAdjusted.ToString("0.0"), MetricColor(item.RoleAdjusted));
			Text(282f, y, 62f, item.Firepower.ToString("0.0"), MetricColor(item.Firepower));
			Text(346f, y, 62f, item.Utility.ToString("0.0"), MetricColor(item.Utility));
			Text(410f, y, 52f, item.IGLImpact.ToString("0.0"), MetricColor(item.IGLImpact));
			Text(464f, y, Math.Max(190f, w - 448f), TranslateVerdict(item.Verdict), MetricColor(item.RoleAdjusted));
			y += 18f;
		}
		y += 6f;
	}

	private void DrawFooter(ref float y, float w)
	{
		ModConfig? config = _config;
		if (config == null || !config.ShowDebugInfo.Value)
		{
			return;
		}
		y += 4f;
		if (_provider is IDiagnosticRosterProvider diagnosticRosterProvider)
		{
			Color color = (diagnosticRosterProvider.LastReadWasLive ? Good() : Warn());
			Text(16f, y, w, $"Live: {(diagnosticRosterProvider.LastReadWasLive ? "OK" : "ожидание")} · источник: {TranslateSource(diagnosticRosterProvider.LastSource)} · основа {diagnosticRosterProvider.LastStarterCount} · запас {diagnosticRosterProvider.LastBenchCount}", color);
			y += 18f;
			if (!diagnosticRosterProvider.LastReadWasLive)
			{
				Text(16f, y, w, "Открой экран «Состав» и подожди, пока SquadList.Init отдаст live-данные.", Warn());
				y += 18f;
			}
		}
	}

	private void DrawMetric(ref float y, string name, double value)
	{
		Text(16f, y, 140f, name + ":", White());
		Text(158f, y, 62f, value.ToString("0.0"), MetricColor(value));
		Text(224f, y, 255f, Spark(value), MetricColor(value));
		y += 20f;
	}

	private static string Spark(double value)
	{
		int num = Math.Clamp((int)Math.Round(value / 2.0), 0, 10);
		return new string('█', num) + new string('░', 10 - num);
	}

	private void Text(float x, float y, float width, string text, Color color)
	{
		Color color2 = GUI.color;
		ModConfig? config = _config;
		if (config != null && config.DrawTextShadow.Value)
		{
			GUI.color = new Color(0f, 0f, 0f, 1f);
			GUI.Label(new Rect(x + 1f, y + 1f, width, 20f), text ?? string.Empty);
		}
		GUI.color = color;
		GUI.Label(new Rect(x, y, width, 20f), text ?? string.Empty);
		GUI.color = color2;
	}

	private void KeepWindowOnScreen()
	{
		if (_config == null)
		{
			return;
		}
		try
		{
			if (_config.AutoFitPanel.Value)
			{
				((Rect)(ref _windowRect)).width = Math.Clamp(((Rect)(ref _windowRect)).width, _config.MinPanelWidth.Value, _config.MaxPanelWidth.Value);
				if (_minimized)
				{
					((Rect)(ref _windowRect)).height = 42f;
				}
				else
				{
					((Rect)(ref _windowRect)).height = Math.Clamp(((Rect)(ref _windowRect)).height, _config.MinPanelHeight.Value, _config.MaxPanelHeight.Value);
				}
			}
			float num = Math.Max(0.5f, _config.Scale.Value);
			float max = Math.Max(20f, (float)Screen.width / num - ((Rect)(ref _windowRect)).width - 20f);
			float max2 = Math.Max(20f, (float)Screen.height / num - ((Rect)(ref _windowRect)).height - 20f);
			((Rect)(ref _windowRect)).x = Math.Clamp(((Rect)(ref _windowRect)).x, 20f, max);
			((Rect)(ref _windowRect)).y = Math.Clamp(((Rect)(ref _windowRect)).y, 20f, max2);
		}
		catch
		{
		}
	}

	private void ResizePanel(int direction)
	{
		if (_config != null)
		{
			float value = ((Rect)(ref _windowRect)).width + _config.ResizeStepWidth.Value * (float)direction;
			float value2 = (_minimized ? Math.Max(_expandedHeight, _config.PanelHeight.Value) : ((Rect)(ref _windowRect)).height) + _config.ResizeStepHeight.Value * (float)direction;
			((Rect)(ref _windowRect)).width = Math.Clamp(value, _config.MinPanelWidth.Value, _config.MaxPanelWidth.Value);
			_expandedHeight = Math.Clamp(value2, _config.MinPanelHeight.Value, _config.MaxPanelHeight.Value);
			if (!_minimized)
			{
				((Rect)(ref _windowRect)).height = _expandedHeight;
			}
			_config.PanelWidth.Value = ((Rect)(ref _windowRect)).width;
			_config.PanelHeight.Value = _expandedHeight;
			KeepWindowOnScreen();
			PersistWindowGeometry();
		}
	}

	private void ResetWindow()
	{
		if (_config != null)
		{
			_minimized = false;
			_draggingWindow = false;
			_expandedHeight = 760f;
			_windowRect = new Rect(56f, 36f, 1120f, 760f);
			PersistWindowGeometry();
			KeepWindowOnScreen();
		}
	}

	private void RefreshReport(bool forceLog)
	{
		if (_provider == null || _scorer == null || _config == null)
		{
			return;
		}
		try
		{
			RosterSnapshot rosterSnapshot = _provider.GetRosterSnapshot();
			_cachedReport = _scorer.Analyze(rosterSnapshot);
			if (!forceLog && !_config.LogOnlyMode.Value)
			{
				return;
			}
			ManualLogSource log = _log;
			bool flag = default(bool);
			if (log != null)
			{
				log.LogInfo($"RosterIntel report: Team={_cachedReport.TeamName}, TeamFit={_cachedReport.TeamFit:0.0}, Firepower={_cachedReport.Firepower:0.0}, IGL={_cachedReport.IGL:0.0}, Starters={_cachedReport.StarterCount}, Weakest={_cachedReport.WeakestLink}, BestSwap={_cachedReport.BestSwap}, Verdict={_cachedReport.Verdict}");
			}
			foreach (PlayerScores player in _cachedReport.Players)
			{
				log = _log;
				if (log != null)
				{
					log.LogInfo($"  {player.Nick} role={player.Role} roleAdj={player.RoleAdjusted:0.0} Perf={player.Performance:0.0} FP={player.Firepower:0.0} AWP={player.AWP:0.0} IGLImpact={player.IGLImpact:0.0} Utility={player.Utility:0.0} verdict={player.Verdict} reason={player.Reason}");
				}
			}
		}
		catch (Exception ex)
		{
			ManualLogSource? log2 = _log;
			if (log2 != null)
			{
				log2.LogWarning((object)("RosterIntel report refresh failed: " + ex));
			}
		}
	}

	private static string SafeName(string s)
	{
		if (!string.IsNullOrWhiteSpace(s))
		{
			return s;
		}
		return "N/A";
	}

	private static string TranslateSwap(string s)
	{
		if (!string.IsNullOrWhiteSpace(s))
		{
			switch (s)
			{
			case "N/A":
				break;
			case "No clear upgrade":
				return "нет явного апгрейда в запасе — сначала проверь основу минимум на 5 прак-картах или в турнире";
			case "No bench candidates":
				return "нет игроков в запасе — трансфер/скаутинг нужен отдельным этапом";
			case "Not calculated":
				return "не рассчитано";
			default:
				return s.Replace("No clear upgrade", "нет явного апгрейда в запасе");
			}
		}
		return "нет данных";
	}

	private static string TranslateVerdict(string v)
	{
		return v switch
		{
			"Elite title contender" => "элитный претендент: сначала проверяй в турнире, не ломай без явной причины", 
			"Elite roster profile" => "элитный профиль: сильная основа, апгрейды только точечно", 
			"Elite core, keep AWPer" => "элитное ядро: AWPer трогать только ради superstar", 
			"Great firepower, IGL ceiling risk" => "огня много, но слабый IGL может ограничить потолок", 
			"Strong structure, check second-star firepower" => "структура есть; проверь второго фрагера минимум на 5 прак-картах", 
			"Contender with stable AWP core" => "уровень претендента: AWP-ядро стабильное", 
			"Contender-level fit" => "уровень претендента: сыграй блок матчей перед перестройкой", 
			"Needs upgrade" => "нужен апгрейд: есть слабое место в основе", 
			"Elite tactical IGL" => "IGL-ядро: сильная структура и почти без потери огня", 
			"Strong IGL, acceptable firepower tax" => "сильный IGL: даёт систему, потеря огня терпима", 
			"Good IGL, watch firepower tax" => "хороший IGL: структура есть, но следи за личным уроном", 
			"Strong IGL fit" => "сильный IGL-фит: роль закрыта хорошо", 
			"Situational IGL" => "IGL рабочий: даёт план, но не тащит один; нужны сильные стрелки рядом", 
			"IGL tax concern" => "IGL дорог по огню: структура есть, но фрагов слишком мало", 
			"Elite AWPer - keep" => "элитный AWPer: не менять, это ядро команды", 
			"Strong AWPer - keep unless superstar upgrade" => "сильный AWPer: менять только на явного superstar", 
			"Strong AWPer" => "сильный AWPer: роль снайпера закрыта хорошо", 
			"Solid AWPer" => "нормальный AWPer: играть можно, но апгрейд возможен", 
			"AWP upgrade target" => "слабое AWP-место: ищи снайпера сильнее", 
			"High-value support" => "ценный support: гранаты и командная работа дают пользу", 
			"Stable support" => "стабильный support: роль закрыта, апгрейд не срочный", 
			"Support upgrade target" => "support на апгрейд: utility/командная польза ниже нужного", 
			"Strong lurker" => "сильный lurker: создаёт давление и может клатчить", 
			"Useful lurker" => "полезный lurker: роль выполняет, но не звезда", 
			"Lurker upgrade target" => "lurker на апгрейд: мало давления или late-round impact", 
			"Star rifler" => "star rifler: главный источник огня", 
			"Star rifler - primary carry" => "star rifler: главный carry, строить атаки вокруг него", 
			"Damage rifler - main gun" => "star-rifler: главный rifle-урон, первый кандидат на star-role", 
			"Structure rifler - utility brain" => "структурный rifler: даёт utility/позиционку и держит систему", 
			"Utility rifler - role balance" => "utility rifler: меньше carry, больше пользы через роль и позиции", 
			"Clutch rifler - late rounds" => "clutch rifler: ценен в поздних раундах и концовках", 
			"Two-way rifler - fire+utility" => "двусторонний rifler: совмещает огонь и utility", 
			"Strong rifler" => "сильный rifler: стабильно держит уровень основы", 
			"Strong rifler - stable core" => "сильный rifler: стабильное ядро без явной слабости", 
			"Role rifler - useful system piece" => "ролевой rifler: полезен системе, но не star-carry", 
			"Solid starter" => "крепкий игрок основы: норм, но не явный лидер", 
			"Upgrade candidate" => "кандидат на апгрейд: слабее требований своей роли", 
			_ => v, 
		};
	}

	private static string TranslateTransferRadarStatus(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return "";
		}
		return s.Replace("scouting-pool: этот состав добавлен в пул наблюдения", "scouting: игроки этого состава добавлены в пул наблюдения").Replace("спортивный transfer radar", "спортивный transfer radar").Replace("деньги/контракты игрок проверяет сам", "деньги/контракты игрок проверяет сам")
			.Replace("пул пуст", "пул пуст")
			.Replace("read-only", "read-only")
			.Replace("цена/контракт пока не читаются", "цена/контракт пока не читаются");
	}

	private static string TranslateTransferTier(string s)
	{
		if (!(s == "priority"))
		{
			if (s == "trial")
			{
				return "тест";
			}
			return "следить";
		}
		return "приоритет";
	}

	private static string TranslateTransferProfile(string s)
	{
		return s switch
		{
			"AWP target" => "AWP-цель", 
			"IGL/structure target" => "IGL/структура", 
			"star-rifler target" => "star-rifler", 
			"system/utility target" => "system/utility", 
			"clutch target" => "clutch", 
			"rifle upgrade watch" => "rifle upgrade", 
			"role-fit watch" => "role-fit", 
			_ => s, 
		};
	}

	private static string TranslateTransferAction(string s)
	{
		return s switch
		{
			"sport priority: compare in game" => "спортивный приоритет", 
			"scout + 5 prac maps" => "тест: скаутинг + 5 прак-карт", 
			"add to watchlist" => "watchlist", 
			"watch-only: AWP role closed" => "только следить: AWP закрыт", 
			"plan restructure" => "планировать перестройку", 
			"scout structure fit" => "скаутить структуру", 
			"scout role-change first" => "сначала проверить смену роли", 
			_ => string.IsNullOrWhiteSpace(s) ? "action?" : s, 
		};
	}

	private static string TranslateTransferConfidence(string s)
	{
		if (!(s == "high"))
		{
			if (s == "medium")
			{
				return "уверенность средняя";
			}
			return "уверенность низкая";
		}
		return "уверенность высокая";
	}

	private static string TranslateTransferEvidence(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return "evidence?";
		}
		return s.Replace("attrs-only", "только attrs").Replace("signals", "сигналы").Replace("maps", "карт");
	}

	private static string TranslateTransferLane(string s)
	{
		return s switch
		{
			"role-safe" => "role-safe", 
			"luxury-awp" => "luxury AWP", 
			"structure-shift" => "перестройка структуры", 
			"role-change" => "смена роли", 
			_ => string.IsNullOrWhiteSpace(s) ? "role?" : s, 
		};
	}

	private static string TranslateTransferRisk(string s)
	{
		if (string.IsNullOrWhiteSpace(s) || s == "role-safe")
		{
			return "";
		}
		return s.Replace("low-evidence", "мало данных").Replace("luxury: AWP role already closed", "AWP уже закрыт").Replace("role-change", "смена роли")
			.Replace("structure-change", "смена структуры")
			.Replace("IGL-risk", "риск IGL");
	}

	private static string BuildEvidenceHint(RosterReport r)
	{
		if (r.StatsCoverage <= 0.1)
		{
			if (!r.IsManagedTeamFocus)
			{
				return " · scouting live attrs";
			}
			return " · моя команда: live attrs";
		}
		if (r.IsManagedTeamFocus && r.ObjectiveConfidence >= 99.5)
		{
			return " · моя команда: full roster intel";
		}
		if (r.StatsCoverage < 80.0)
		{
			return " · partial stats";
		}
		if (r.ObjectiveConfidence >= 99.5)
		{
			return " · full profile/tier stats+attrs";
		}
		if (r.DirectStatsPlayers >= Math.Max(1, r.StarterCount - 1))
		{
			return " · direct stats+attrs";
		}
		if (r.LogStatsPlayers > 0)
		{
			return " · tier/log stats+attrs";
		}
		return " · stats+attrs";
	}

	private static string BuildEvidenceSources(RosterReport r)
	{
		int value = Math.Max(1, r.StarterCount);
		string value2 = (r.IsManagedTeamFocus ? "Фокус: моя команда" : "Режим: scouting");
		string value3 = (string.IsNullOrWhiteSpace(r.ManagedTeamSource) ? "" : (" (" + r.ManagedTeamSource + ")"));
		return $"{value2}{value3} | Сигналы: profile/direct {r.DirectStatsPlayers}/{value}, tier/log {r.LogStatsPlayers}/{value}, attrs-only {r.AttributeOnlyPlayers}/{value}";
	}

	private static Color ReleaseAuditColor(RosterReport r)
	{
		if (r.IsManagedTeamFocus && r.ObjectiveConfidence >= 99.5 && r.StarterCount >= 5 && r.StatsCoverage >= 99.5)
		{
			return Good();
		}
		if (r.IsManagedTeamFocus && r.StarterCount >= 5)
		{
			return Average();
		}
		return Muted();
	}

	private static string BuildCompactChecklistLine(RosterReport r)
	{
		string value = (r.IsManagedTeamFocus ? "OK: моя команда" : "scouting: чужой состав");
		string value2 = ((r.StarterCount >= 5) ? "состав 5/5" : $"состав {r.StarterCount}/5");
		int num = (int)Math.Round(r.StatsCoverage * (double)Math.Max(1, r.StarterCount) / 100.0);
		string value3 = ((num >= r.StarterCount) ? $"статы {num}/{Math.Max(1, r.StarterCount)}" : $"статы {num}/{Math.Max(1, r.StarterCount)} — добери данные");
		string value4 = ((r.IsManagedTeamFocus && r.ObjectiveConfidence >= 99.5) ? "готов к 100%" : (r.IsManagedTeamFocus ? "фокус OK, нужны статы" : "не финальный вывод"));
		return $"Чеклист: {value}; {value2}; {value3}; {value4}";
	}

	private static string BuildMatchHistoryText(RosterReport r)
	{
		return (r.MatchHistoryStatus ?? "unknown").Replace("new-save/no-match-history: using full live attributes", "матчей ещё нет — вывод по live-атрибутам; после 5 прак-карт/турнира станет точнее").Replace("no-match-history: scouting uses live attributes only", "матчей нет — scouting только по live-атрибутам").Replace("profile stats", "profile-статы")
			.Replace("no tier match history yet", "tier-истории ещё нет")
			.Replace("partial tier history", "частичная tier-история")
			.Replace("tier history", "tier-история")
			.Replace("profile/tier signals", "profile/tier-сигналы")
			.Replace("live attributes only", "только live-атрибуты")
			.Replace("maps", "карт")
			.Replace("no live roster", "нет live-состава");
	}

	private static string BuildNextStepLine(RosterReport r)
	{
		bool flag = r.IsManagedTeamFocus && r.StarterCount >= 5 && r.StatsCoverage >= 99.0;
		bool flag2 = string.Equals(r.BestSwap, "No bench candidates", StringComparison.OrdinalIgnoreCase);
		bool flag3 = string.Equals(r.BestSwap, "No clear upgrade", StringComparison.OrdinalIgnoreCase);
		bool flag4 = r.TotalTierMaps <= 0;
		if (flag && (flag2 || flag3) && r.TeamFit >= 16.5)
		{
			if (flag4)
			{
				return "состав рабочий; сыграй минимум 5 прак-карт или полный турнир и сравни форму, impact и роли";
			}
			return "не ломай основу сейчас; проверь её в полноценном турнире, затем меняй только при повторной просадке роли/статистики";
		}
		if (flag && !flag2 && !string.IsNullOrWhiteSpace(r.BestSwap) && r.BestSwap != "N/A")
		{
			return "есть bench-вариант; протестируй его минимум на 5 прак-картах, затем сверяй с официальными картами";
		}
		if (!r.IsManagedTeamFocus)
		{
			return "это scouting-оценка: смотри как ориентир, для трансфера нужны цена, контракт и свежая форма";
		}
		if (r.StatsCoverage < 99.0)
		{
			return "сначала открой/обнови статистику всех 5 игроков, потом оценивай замену";
		}
		return "сыграй минимум 5 прак-карт или полный турнир и обнови отчёт; разовая цифра не должна ломать основу";
	}

	private static string BuildAuditLine(RosterReport r)
	{
		string text = TranslateAudit(r.ObjectiveAudit);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "нет данных";
		}
		return "Аудит объективности: " + text;
	}

	private static string BuildPrecisionChecklistLine(RosterReport r)
	{
		string text = TranslatePrecisionChecklist(r.PrecisionChecklist);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "нет данных";
		}
		return "Чеклист 100%: " + text;
	}

	private static string BuildPlayerEvidenceLine(RosterReport r)
	{
		if (r.Players.Count == 0)
		{
			return "Ev: нет игроков";
		}
		string[] value = (from p in r.Players.Take(5)
			select p.Nick + "=" + p.EvidenceTag).ToArray();
		return "Ev игроки: " + string.Join(" | ", value) + "   D=profile, T=tier/log, A=attrs";
	}

	private static string TranslatePrecisionChecklist(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return "";
		}
		return s.Replace("OK own-team", "OK моя команда").Replace("SCOUTING cap", "scouting cap").Replace("OK roster", "OK состав")
			.Replace("BLOCK roster", "БЛОК состав")
			.Replace("OK stats", "OK статы")
			.Replace("BLOCK stats", "БЛОК статы")
			.Replace("missing", "нет статов")
			.Replace("direct", "direct")
			.Replace("tier/log", "tier/log")
			.Replace("history maps", "история карт")
			.Replace("avg signals", "ср. сигналы")
			.Replace("READY 100", "готово к 100%")
			.Replace("FOCUS OK / ANALYTICS WAITING", "фокус OK / аналитика ждёт статистику")
			.Replace("BLOCK: нет live-состава", "БЛОК: нет live-состава");
	}

	private static string TranslateAudit(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return "";
		}
		return s.Replace("managed-team", "моя команда").Replace("scouting", "скаутинг").Replace("stats", "статы")
			.Replace("direct", "direct")
			.Replace("tier/log", "tier/log")
			.Replace("tier maps", "tier maps")
			.Replace("match history", "история матчей")
			.Replace("100% allowed for managed team", "устаревший флаг 100%")
			.Replace("100% подтверждено статистикой своей команды", "100% подтверждено статистикой своей команды")
			.Replace("полный состав есть, но глубина статистики ещё не идеальная", "полный состав есть, но глубина статистики ещё не идеальная")
			.Replace("opponent scouting cap", "чужая команда ограничена scouting-cap")
			.Replace("bench-only", "только запас")
			.Replace("managed-team", "моя команда")
			.Replace("weakest", "слабое место")
			.Replace("team+role", "team+role")
			.Replace("no clear upgrade", "нет явного апгрейда в запасе")
			.Replace("No clear upgrade", "нет явного апгрейда в запасе")
			.Replace("candidate", "кандидат");
	}

	private static string BuildMatchHistoryLine(RosterReport r)
	{
		string text = r.MatchHistoryStatus ?? "unknown";
		text = text.Replace("new-save/no-match-history: using full live attributes", "новый сейв/матчей нет: точные live-атрибуты").Replace("no-match-history: scouting uses live attributes only", "матчей нет: scouting только по live-атрибутам").Replace("profile stats", "profile-статы")
			.Replace("no tier match history yet", "tier-истории матчей ещё нет")
			.Replace("partial tier history", "частичная tier-история")
			.Replace("tier history", "tier-история")
			.Replace("profile/tier signals", "profile/tier-сигналы")
			.Replace("live attributes only", "только live-атрибуты")
			.Replace("maps", "карт")
			.Replace("no live roster", "нет live-состава");
		return "История матчей: " + text;
	}

	private static string TranslateDataConfidence(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return "неизвестно";
		}
		return s.Replace("managed-team evidence: stats", "моя команда: доказанные статы").Replace("managed-team: stats", "моя команда: статы").Replace("high: stats", "высокое: статы")
			.Replace("good: stats", "хорошее: статы")
			.Replace("partial: stats", "частичное: статы")
			.Replace("attribute-only: stats", "только атрибуты: статы")
			.Replace("confidence", "объективность")
			.Replace("no live starters", "нет live-основы");
	}

	private static string TranslateSource(string s)
	{
		return (s ?? "none").Replace("captured:", "захвачено: ").Replace("hook:SquadList.Init(bench)", "hook список состава").Replace("hook:SquadList.Init(main)", "hook основа")
			.Replace("PlayerSquadCard UI", "карточки состава")
			.Replace("fallback", "fallback/sample");
	}

	private static string TranslateStatus(string s)
	{
		return (s ?? "").Replace("live OK via", "live успешно через").Replace("live not available", "live недоступен");
	}

	private static Color White()
	{
		return new Color(1f, 1f, 1f, 1f);
	}

	private static Color Cyan()
	{
		return new Color(0.15f, 0.95f, 1f, 1f);
	}

	private static Color Muted()
	{
		return new Color(0.75f, 0.85f, 0.95f, 1f);
	}

	private static Color Elite()
	{
		return new Color(0.35f, 1f, 0.95f, 1f);
	}

	private static Color Good()
	{
		return new Color(0.15f, 1f, 0.25f, 1f);
	}

	private static Color Solid()
	{
		return new Color(0.55f, 1f, 0.3f, 1f);
	}

	private static Color Average()
	{
		return new Color(1f, 0.86f, 0.2f, 1f);
	}

	private static Color Weak()
	{
		return new Color(1f, 0.55f, 0.16f, 1f);
	}

	private static Color Bad()
	{
		return new Color(1f, 0.35f, 0.35f, 1f);
	}

	private static Color Warn()
	{
		return Average();
	}

	private static Color MetricColor(double v)
	{
		if (!(v >= 19.0))
		{
			if (!(v >= 17.5))
			{
				if (!(v >= 16.0))
				{
					if (!(v >= 14.5))
					{
						if (!(v >= 12.5))
						{
							return Bad();
						}
						return Weak();
					}
					return Average();
				}
				return Solid();
			}
			return Good();
		}
		return Elite();
	}

	private static Color DataConfidenceColor(double coverage)
	{
		if (!(coverage >= 80.0))
		{
			if (!(coverage >= 40.0))
			{
				return Bad();
			}
			return Average();
		}
		return Good();
	}

	private static Color ObjectiveConfidenceColor(double confidence)
	{
		if (!(confidence >= 92.0))
		{
			if (!(confidence >= 80.0))
			{
				if (!(confidence >= 68.0))
				{
					return Bad();
				}
				return Average();
			}
			return Solid();
		}
		return Good();
	}

	private static Color EvidenceSourcesColor(RosterReport r)
	{
		if (!r.IsManagedTeamFocus || !(r.ObjectiveConfidence >= 99.5))
		{
			if (!(r.ObjectiveConfidence >= 99.5))
			{
				if (r.DirectStatsPlayers < Math.Max(1, r.StarterCount - 1))
				{
					if (r.LogStatsPlayers <= 0)
					{
						return Muted();
					}
					return Average();
				}
				return Good();
			}
			return Good();
		}
		return Good();
	}

	private static Color MatchHistoryColor(RosterReport r)
	{
		if (r.TotalTierMaps <= 0)
		{
			if (!r.IsManagedTeamFocus)
			{
				return Muted();
			}
			return Average();
		}
		return Good();
	}

	private static Color PrecisionChecklistColor(RosterReport r)
	{
		if (!r.IsManagedTeamFocus || r.StarterCount < 5 || r.AttributeOnlyPlayers > 0)
		{
			if (!r.IsManagedTeamFocus)
			{
				return Muted();
			}
			return Average();
		}
		return Good();
	}

	private static Color EvidenceTagColor(string tag)
	{
		if (!string.IsNullOrWhiteSpace(tag))
		{
			if (!tag.Contains("D", StringComparison.OrdinalIgnoreCase) || !tag.Contains("T", StringComparison.OrdinalIgnoreCase))
			{
				if (!tag.Contains("D", StringComparison.OrdinalIgnoreCase))
				{
					if (!tag.Contains("T", StringComparison.OrdinalIgnoreCase))
					{
						if (!tag.Contains("S", StringComparison.OrdinalIgnoreCase))
						{
							return Muted();
						}
						return Solid();
					}
					return Average();
				}
				return Good();
			}
			return Elite();
		}
		return Muted();
	}

	private static Color RoleColor(string role)
	{
		if (!role.Contains("IGL", StringComparison.OrdinalIgnoreCase))
		{
			if (!role.Contains("AWP", StringComparison.OrdinalIgnoreCase))
			{
				return new Color(0.55f, 0.75f, 1f, 1f);
			}
			return new Color(0.45f, 0.85f, 1f, 1f);
		}
		return Cyan();
	}
}
