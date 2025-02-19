﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using KModkit;
using Random = UnityEngine.Random;
using System.Text.RegularExpressions;

public class SubtractNauseamScript : MonoBehaviour {

	public KMSelectable[] directionsSelectable;
	public KMAudio mAudio;
	public KMBombModule modSelf;
	public KMBombInfo info;
	public TextMesh[] directionsText;
	public TextMesh centerMesh, timerMesh, statusMesh;

	static readonly string[] debugDirections = { "Up", "Right", "Down", "Left" },
		debugQuestionType = { "Q#", "ODD/EVEN", "MIN", "MAX", "Xo#", "#o#" },
		directionSymbols = { "\u2191", "\u2192", "\u2193", "\u2190" };
	static readonly string[] symbolsGrid = {
		"-----£%-----",
		"----#$&£----",
		"---$&£#%$---",
		"--£#%&$&£%--",
		"-&$&£#%#$&£-",
		"£#%#$&£&%#$&",
		"$&£&%#$#£&%£",
		"-%$#£&%&$#$-",
		"--%&$#£#%&--",
		"---£%&$&£---",
		"----$#%#----",
		"-----&$-----",
	};


	static int modIDcnt;
	int modID;
	List<Result> allResults = new List<Result>();
	/*
	List<bool> lastAttemptsSuccessful = new List<bool>();
	List<float> lastAttemptsTimeTaken = new List<float>();
	List<int> lastAttemptsQuestionsAnswered = new List<int>();
	List<int[]> lastAttemptsExpectedFinalAnswer = new List<int[]>();
	List<int[]> lastAttemptsSubmittedFinalAnswer = new List<int[]>();
	*/
	float timeTaken;
	bool hasStarted = false, isAnimating, firstActivation = false, modSolved = false, showingPassword = false, storeAllResults = true, allowTPSayPrompt, TwitchPlaysActive;
	int[] generatedIndividualDigits, generatedDirectionIdxes, allQuestionIDxType, expectedSubmissionIdx, currentSubmissionIdx, directionInputDigits;
	int curQuestionIdx = 0, lastHighlightedIdx = -1, curResultHighlightIdx, maxItemsStored, oopsCount;
	string[] lastArrowDisplayedTexts;
	string selectedCharacters = "0123";
	IEnumerator timeTicker;

	SubtractNauseamSettings localSettings = new SubtractNauseamSettings();
	private IDictionary<string, object> tpAPI;

	void TrySendMessage(string message, params object[] args)
    {
		if (TwitchPlaysActive && allowTPSayPrompt)
		{
			if (tpAPI == null)
			{
				GameObject tpAPIGameObject = GameObject.Find("TwitchPlays_Info");
				//To make the module can be tested in test harness, check if the gameObject exists.
				if (tpAPIGameObject != null)
					tpAPI = tpAPIGameObject.GetComponent<IDictionary<string, object>>();
				else
					return;
			}
			tpAPI["ircConnectionSendMessage"] = string.Format(message, args);
		}
	}
	private string GetModuleCode()
	{
		Transform closest = null;
		float closestDistance = float.MaxValue;
		foreach (Transform children in transform.parent)
		{
			var distance = (transform.position - children.position).magnitude;
			if (children.gameObject.name == "TwitchModule(Clone)" && (closest == null || distance < closestDistance))
			{
				closest = children;
				closestDistance = distance;
			}
		}

		return closest != null ? closest.Find("MultiDeckerUI").Find("IDText").GetComponent<UnityEngine.UI.Text>().text : null;
	}
	void Awake()
    {
		try
        {
			var obtainedSettings = new ModConfig<SubtractNauseamSettings>("Subtract Nauseam Settings");
			localSettings = obtainedSettings.Settings;
			obtainedSettings.Settings = localSettings;
			maxItemsStored = maxItemsStored == 0 ? 1 : localSettings.maxStoredResultsAll;
			storeAllResults = !localSettings.storeSuccessfulResultsOnly;
		}
		catch
        {
			Debug.LogWarning("[Subtract Nauseam Settings] Settings do not work as intended! Using default settings!");
			maxItemsStored = -1;
			storeAllResults = true;
        }
    }

	// Use this for initialization
	void Start()
	{
		modID = ++modIDcnt;
		for (var x = 0; x < directionsSelectable.Length; x++)
		{
			var y = x;
			directionsSelectable[x].OnInteract += delegate {
				directionsSelectable[y].AddInteractionPunch(0.5f);
				if (!isAnimating)
				{
					if (hasStarted)
						HandleCurrentPressInQuizMode(y);
					else if (!firstActivation)
					{
						firstActivation = true;
						StartAttempt();
					}
					else
                    {
						HandlePressDuringStatus(y);
                    }
				}
				return false;
			};
			directionsSelectable[x].OnHighlight += delegate {

				if (hasStarted && !isAnimating && curQuestionIdx < 10)
				{
					lastHighlightedIdx = y;
					HandleHighlightAnswer(y);
				}
			};
			directionsSelectable[x].OnHighlightEnded += delegate {
				if (hasStarted && !isAnimating && curQuestionIdx < 10)
					HandleHighlightAnswer();
			};
		}
		GenerateStuff();
	}
	void QuickLog(string value, params object[] args)
	{
		Debug.LogFormat("[Subtract Nauseam #{0}] {1}", modID, string.Format(value, args));
	}
	void CreateCurQuestion()
	{
		if (lastArrowDisplayedTexts == null)
			lastArrowDisplayedTexts = new string[4];
		var incorrectDirectionIdxes = Enumerable.Range(0, 4).Where(a => generatedDirectionIdxes[curQuestionIdx] != a);
		var curCorrectValue = generatedIndividualDigits[curQuestionIdx];
		var incorrectDigits = Enumerable.Range(0, 10).Where(a => a != curCorrectValue).ToArray().Shuffle();
		var possibleCenterText = "OOPS";
		switch (allQuestionIDxType[curQuestionIdx])
		{
			case 0:
				{
					possibleCenterText = "Q#";
				}
				goto default;
			case 1:
				{
					incorrectDigits = incorrectDigits.Where(a => a % 2 != curCorrectValue % 2).ToArray();
					possibleCenterText = curCorrectValue % 2 == 1 ? "ODD" : "EVEN";
				}
				goto default;
			case 2:
				{
					incorrectDigits = incorrectDigits.Where(a => a > curCorrectValue).ToArray();
					possibleCenterText = "MIN";
				}
				goto default;
			case 3:
				{
					incorrectDigits = incorrectDigits.Where(a => a < curCorrectValue).ToArray();
					possibleCenterText = "MAX";
				}
				goto default;
			case 4:
				{
					var lastValue = generatedIndividualDigits[curQuestionIdx - 1];
					possibleCenterText = string.Format("X{0}{1}", lastValue - curCorrectValue > 0 ? "-" : lastValue - curCorrectValue < 0 ? "+" : "", lastValue == curCorrectValue ? "" : lastValue >= curCorrectValue ? (lastValue - curCorrectValue).ToString() : (curCorrectValue - lastValue).ToString());
				}
				goto default;
			case 5:
				{
					var modifier = Random.Range(1, 10);
					var possibleExpressions = new[] {
						string.Format("{0}+{1}", curCorrectValue - modifier, modifier),
						string.Format("{0}/{1}", curCorrectValue * modifier, modifier),
						string.Format("{0}-{1}", curCorrectValue + modifier, modifier),
						//string.Format("{0}*{1}", curCorrectValue / modifier, modifier)
					};
					var possibleValues = Enumerable.Range(0, 3).ToList();
					if (curCorrectValue - modifier <= 0)
						possibleValues.Remove(0);
					/*
					if (curCorrectValue % modifier != 0)
						possibleValues.Remove(3);
					*/
					var selectedIdxPossible = possibleValues.PickRandom();
					possibleCenterText = possibleExpressions[selectedIdxPossible];
				}
				goto default;
			default:
				centerMesh.text = possibleCenterText;
				statusMesh.text = string.Format("{0}/10", allQuestionIDxType[curQuestionIdx] != 0 ? (curQuestionIdx + 1).ToString("00") : "??");
				lastArrowDisplayedTexts = directionsText.Select(a => a.text).ToArray();
				for (var x = 0; x < incorrectDirectionIdxes.Count(); x++)
					lastArrowDisplayedTexts[incorrectDirectionIdxes.ElementAt(x)] = incorrectDigits[x].ToString();
				lastArrowDisplayedTexts[generatedDirectionIdxes[curQuestionIdx]] = curCorrectValue.ToString();
				for (var x = 0; x < directionsText.Length; x++)
					directionsText[x].text = lastArrowDisplayedTexts[x];
				HandleHighlightAnswer(lastHighlightedIdx);
				QuickLog("Showing prompt \"{0}\" with following answers: {1}", possibleCenterText, Enumerable.Range(0, 4).Select(a => "[" + debugDirections[a] + ": " + lastArrowDisplayedTexts[a] + "]").Join(", "), curQuestionIdx + 1);
                TrySendMessage("Subtract Nauseum #{2} is showing the following prompt: \"{0}\", with the possible choices: {1}", possibleCenterText, Enumerable.Range(0, 4).Select(a => "[" + debugDirections[a].First() + ": " + lastArrowDisplayedTexts[a] + "]").Join(", "), GetModuleCode());
				break;
		}
	}
	void HandleCurrentPressInQuizMode(int idxDirectionPressed)
	{
		if (curQuestionIdx < 10)
		{
			if (generatedDirectionIdxes[curQuestionIdx] == idxDirectionPressed)
			{
				curQuestionIdx++;
				mAudio.PlaySoundAtTransform("Right", directionsSelectable[idxDirectionPressed].transform);
				if (curQuestionIdx < 10)
					CreateCurQuestion();
				else
					StartSubmission();
			}
			else
			{
				QuickLog("Direction {0} was incorrectly pressed on prompt #{1}.", debugDirections[idxDirectionPressed], curQuestionIdx + 1);
				mAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, directionsSelectable[idxDirectionPressed].transform);
				ResetModule();
			}
		}
		else
		{
			mAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, directionsSelectable[idxDirectionPressed].transform);
			if (directionInputDigits[idxDirectionPressed] == 3)
			{
				QuickLog("The symbol representing the submit button was pressed and submitted the following passcode: {0}", currentSubmissionIdx.Select(a => a < 0 || a >= selectedCharacters.Length ? "-" : selectedCharacters.Substring(a, 1)).Join(""));
				if (currentSubmissionIdx.SequenceEqual(expectedSubmissionIdx))
				{
					if (!modSolved)
					{
						modSolved = true;
						modSelf.HandlePass();
						QuickLog("At this point, the module has been disarmed. Further attempts will be logged underneath this, with fake strikes only being noted.");
					}
					if (oopsCount > 0 || Random.value > 0.2f || timeTaken >= 80f)
						mAudio.PlaySoundAtTransform("Good", transform);
					else
						mAudio.PlaySoundAtTransform("Best", transform);
					StopCoroutine(timeTicker);
					StoreResult(true, timeTaken, curQuestionIdx, expectedSubmissionIdx.ToArray(), currentSubmissionIdx.ToArray());
					curResultHighlightIdx = allResults.Count - 1;
					ShowStatus();
					GenerateStuff();
					hasStarted = false;
				}
				else
				{
					ResetModule();
				}
			}
			else
			{
				for (var x = 0; x < currentSubmissionIdx.Length - 1; x++)
				{
					currentSubmissionIdx[x] = currentSubmissionIdx[x + 1];
				}
				currentSubmissionIdx[3] = directionInputDigits[idxDirectionPressed];
				statusMesh.text = currentSubmissionIdx.Select(a => a < 0 || a >= selectedCharacters.Length ? "-" : selectedCharacters.Substring(a, 1)).Join("");
			}
		}
	}
	void GenerateStuff()
    {
		if (generatedDirectionIdxes == null)
			generatedDirectionIdxes = new int[10];
		if (generatedIndividualDigits == null)
			generatedIndividualDigits = new int[10];
		if (expectedSubmissionIdx == null)
			expectedSubmissionIdx = new int[4];
		if (currentSubmissionIdx == null)
			currentSubmissionIdx = new int[4];
		if (directionInputDigits == null)
			directionInputDigits = Enumerable.Range(0, 4).ToArray();
		// Generate a pooled set of questions.
		allQuestionIDxType = new[] { 0, 1, 1, 2, 3, 4, 5, 5, 5, 5 };
		// 0: Q#, 1: ODD/EVEN, 2: MIN, 3: MAX, 4: Xo#, 5: #o#
		do
			allQuestionIDxType.Shuffle();
		while (allQuestionIDxType.First() == 4);
		var directionsFixed = new int[Random.Range(4, 6)];
		for (var x = 0; x < directionsFixed.Length; x++)
        {
			// Pregenerate all correct directions on this module.
			directionsFixed[x] = Random.Range(0, 4);
		}
		for (var x = 0; x < allQuestionIDxType.Length; x++)
		{
			// Pregenerate all correct values on this module.
			switch (allQuestionIDxType[x])
			{
				case 0:
					generatedIndividualDigits[x] = (x + 1) % 10;
					break;
				case 1:
					generatedIndividualDigits[x] = Enumerable.Range(0, 10).Where(a => a % 2 == allQuestionIDxType[x] % 2).PickRandom();
					break;
				case 2:
					generatedIndividualDigits[x] = Random.Range(0, 7);
					break;
				case 3:
					generatedIndividualDigits[x] = Random.Range(3, 10);
					break;
				default:
					generatedIndividualDigits[x] = Random.Range(0, 10);
					break;
			}
			
		}

		var finalDirectionsSet = new List<int>();
		for (var x = 0; x < directionsFixed.Length; x++)
		{
			for (var i = 0; i < 2; i++)
			{
				finalDirectionsSet.Add(directionsFixed[x]);
			}
		}

		while (finalDirectionsSet.Count < 10)
        {
			var isHorizontalCancel = Random.value < 0.5f;
			if (isHorizontalCancel)
            {
				finalDirectionsSet.Add(0);
				finalDirectionsSet.Add(2);
			}
			else
			{
				finalDirectionsSet.Add(1);
				finalDirectionsSet.Add(3);
			}
		}
		finalDirectionsSet.Shuffle();
		generatedDirectionIdxes = finalDirectionsSet.ToArray().Shuffle();
		QuickLog("Generated value sequence: {0}", generatedIndividualDigits.Join(", "));
		QuickLog("Generated directions to press: {0}", generatedDirectionIdxes.Select(a => debugDirections[a]).Join(", "));
		QuickLog("Generated question types: {0}", allQuestionIDxType.Select(a => debugQuestionType[a]).Join(", "));
		var finalSum = generatedIndividualDigits.Sum();
		for (var x = 0; x < 4; x++)
		{
			var curProduct = 1;
			for (var y = 0; y < 3 - x; y++)
				curProduct *= 3;
			expectedSubmissionIdx[x] = finalSum / curProduct % 3;
			currentSubmissionIdx[x] = -1;
		}
		QuickLog("The last 4 ternary digits of the sum of the generated value sequence ({1}) is: {0}", expectedSubmissionIdx.Join(""), finalSum);
		directionInputDigits.Shuffle();
		// Calculate the correct password for this module.

		var startColTL = 5;
		var startRowTL = 5;
		var offsetDirHoriz = directionsFixed.Count(a => a == 1) - directionsFixed.Count(a => a == 3);
		var offsetDirVert = directionsFixed.Count(a => a == 2) - directionsFixed.Count(a => a == 0);
		if (offsetDirHoriz != 0)
			QuickLog("You should move {0} step{1} {2}.", Mathf.Abs(offsetDirHoriz), Mathf.Abs(offsetDirHoriz) == 1 ? "" : "s", offsetDirHoriz < 0 ? "left" : "right");
		else
			QuickLog("You should stay where you are horizontally.");
		if (offsetDirVert != 0)
			QuickLog("...And then move {0} step{1} {2}.", Mathf.Abs(offsetDirVert), Mathf.Abs(offsetDirVert) == 1 ? "" : "s", offsetDirVert < 0 ? "up" : "down");
		else
			QuickLog("...And then stay where you are vertically.");
		var finalObtainedChars = symbolsGrid[startRowTL + offsetDirVert].Substring(startColTL + offsetDirHoriz, 2) + symbolsGrid[startRowTL + offsetDirVert + 1].Substring(startColTL + offsetDirHoriz, 2);
		QuickLog("Obtained characters in reading order: {0}", finalObtainedChars.Join(""));
		//QuickLog("Characters present on the module: {0}", finalObtainedChars.Join(""));

		var distinctSerialNoLetters = info.GetSerialNumberLetters().Distinct().Take(4);
		//Debug.Log(afterModifiedChars.Join(""));
		var orderedItems = Enumerable.Range(0, 4 - distinctSerialNoLetters.Count())
			.Concat(
			Enumerable.Range(0, distinctSerialNoLetters.Count()).OrderByDescending(a => distinctSerialNoLetters.ElementAt(a)).Select(a => a - distinctSerialNoLetters.Count() + 4)).ToArray();
		//Debug.Log(Enumerable.Range(0, 4).Select(a => a.ToString() + ":" + orderedItems.ElementAt(a)).Join("|"));
		selectedCharacters = Enumerable.Range(0, 4).Select(a => finalObtainedChars.ElementAt(orderedItems[a])).Join("");

		QuickLog("After ordering the symbols in relation to the serial number letters, the symbols are represented by the following: [{0}]",
			Enumerable.Range(0, 4).Select(a => a.ToString() + ": " + selectedCharacters[a]).Join("], ["));
		QuickLog("Expected passcode to submit: {0}", expectedSubmissionIdx.Select(a => selectedCharacters[a]).Join(""));
	}
	void ResetModule()
	{
		StopCoroutine(timeTicker);
		if (!modSolved)
		{
			modSelf.HandleStrike();
			if (oopsCount >= 2)
				mAudio.PlaySoundAtTransform("Vom", transform);
			else
				oopsCount++;
		}
		else
			mAudio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.Strike, transform);
		hasStarted = false;
		StoreResult(false, timeTaken, curQuestionIdx, expectedSubmissionIdx.ToArray(), curQuestionIdx >= 10 ? currentSubmissionIdx.ToArray() : null);
		GenerateStuff();
		ShowStatus();
	}
	void StoreResult(bool successful, float timeTaken, int questionsAnswered, int[] expectedSubmission, int[] inputtedSubmission)
    {
		if (successful || storeAllResults)
        {
			var newResult = new Result(successful, timeTaken, questionsAnswered, expectedSubmission, inputtedSubmission);
			if (allResults.Count + 1 > maxItemsStored && maxItemsStored > 0)
				allResults.RemoveAt(0);
			allResults.Add(newResult);
		}
    }

	void HandlePressDuringStatus(int dirIdx)
    {
		switch (dirIdx)
		{
			case 2:
				StartAttempt();
				break;
			case 0:
				if (allResults[curResultHighlightIdx].questionsAnswered >= 10)
				{
					showingPassword = !showingPassword;
					goto default;
				}
				goto case 2;
			case 1:
				if (curResultHighlightIdx + 1 < allResults.Count)
				{
					curResultHighlightIdx = curResultHighlightIdx + 1;
					showingPassword = false;
					goto default;
				}
				goto case 2;

			case 3:
				if (curResultHighlightIdx > 0)
				{
					curResultHighlightIdx = curResultHighlightIdx - 1;
					showingPassword = false;
					goto default;
				}
				goto case 2;
			default:
				ShowStatus();
				break;
		}
	}
	void ShowStatus()
	{
		if (curResultHighlightIdx >= allResults.Count || curResultHighlightIdx < 0)
        {
			timerMesh.text = "999";
			for (var x = 0; x < directionsText.Length; x++)
				directionsText[x].text = "";
			centerMesh.text = "";
			statusMesh.text = "";
			return;
		}

		var curResultShown = allResults[curResultHighlightIdx];
		var timeGrades = new Dictionary<float, string>()
		{
			{ 1000, "D-" },
			{ 925, "D" },
			{ 850, "D+" },
			{ 775, "C-" },
			{ 700, "C" },
			{ 625, "C+" },
			{ 550, "B-" },
			{ 500, "B" },
			{ 450, "B+" },
			{ 400, "A-" },
			{ 350, "A" },
			{ 300, "A+" },
			{ 250, "S-" },
			{ 210, "S" },
			{ 170, "S+" },
			{ 130, "SS" },
			{ 105, "U" },
			{ 80, "X" },
		};
		var expectedGrade = "F";
		if (curResultShown.isSuccessful)
			foreach (var aTimeGrade in timeGrades)
			{
				if (curResultShown.timeTaken < aTimeGrade.Key)
					expectedGrade = aTimeGrade.Value;
			}
		centerMesh.text = expectedGrade;
		directionsText[1].text = curResultHighlightIdx + 1 < allResults.Count ? directionSymbols[1] : "";
		directionsText[3].text = curResultHighlightIdx > 0 ? directionSymbols[3] : "";
		directionsText[2].text = "";
		directionsText[0].text = curResultShown.questionsAnswered >= 10 ? "T" : "";
		if (showingPassword)
		{
			var sum = 0;
            for (var x = 0; x < curResultShown.expectedAnswer.Length; x++)
            {
				sum *= 3;
				sum += curResultShown.expectedAnswer[x];
			}
			timerMesh.text = sum.ToString("00");
			statusMesh.text = curResultShown.submittedAnswer.Select(a => a < 0 ? "-" : a.ToString()).Join("");
			statusMesh.color = curResultShown.isSuccessful ? Color.green : Color.red;
		}
		else
		{
			statusMesh.text = curResultShown.questionsAnswered.ToString("00") + "/10";
			timerMesh.text = curResultShown.timeTaken.ToString("00");
			statusMesh.color = Color.white;
		}
	}
	void StartSubmission()
	{
		QuickLog("Time to submit a passcode.");
		centerMesh.text = "";
		statusMesh.text = "----";
		for (var x = 0; x < directionInputDigits.Length; x++)
		{
			directionsText[x].text = selectedCharacters.Substring(directionInputDigits[x], 1);
		}
		TrySendMessage("Subtract Nauseum #{1} is showing an empty prompt with the following choices: {0}", Enumerable.Range(0, 4).Select(a => "[" + debugDirections[a].First() + ": " + selectedCharacters[directionInputDigits[a]] + "]").Join(" "), GetModuleCode());
	}

	void HandleHighlightAnswer(int idx = -1)
	{
		for (var x = 0; x < directionsText.Length; x++)
			directionsText[x].text = idx == x ? directionSymbols[x] : lastArrowDisplayedTexts[x];
	}
	void StartAttempt()
	{
		lastHighlightedIdx = -1;
		showingPassword = false;
		isAnimating = true;
		timeTicker = CountTimeUp();
		StartCoroutine(timeTicker);
	}
	private IEnumerator CountTimeUp()
	{
		mAudio.PlaySoundAtTransform("Startup", transform);
		for (var x = 0; x < directionsText.Length; x++)
			directionsText[x].text = "";
        
		statusMesh.text = "";
		statusMesh.color = Color.white;
		centerMesh.text = "";
		for (float x = 0f; x < 1f; x += Time.deltaTime)
		{
			timerMesh.text = Mathf.FloorToInt(999 * (1f - x)).ToString("00");
			yield return null;
		}
		isAnimating = false;
		timeTaken = 0;
		curQuestionIdx = 0;
		CreateCurQuestion();
		hasStarted = true;
		while (timeTaken < 999)
		{
			yield return null;
			timeTaken += Time.deltaTime;
			timerMesh.text = timeTaken.ToString("00");
		}
		timeTaken = 999;
		timerMesh.text = "999";
	}
	public class Result	
	{
		public bool isSuccessful;
		public float timeTaken;
		public int questionsAnswered;
		public int[] expectedAnswer, submittedAnswer;
		public Result(bool success, float t, int q, int[] eA, int[] sA)
        {
			expectedAnswer = eA;
			submittedAnswer = sA;
			timeTaken = t;
			isSuccessful = success;
			questionsAnswered = q;
        }
	}

	IEnumerator TwitchHandleForcedSolve()
    {
		if (!hasStarted)
			directionsSelectable[2].OnInteract();
		do
			yield return true;
		while (isAnimating);
		allowTPSayPrompt = false; // Disable this to prevent chat flooding when autosolving.
		for (var x = curQuestionIdx; x < generatedDirectionIdxes.Length; x++)
        {
			directionsSelectable[generatedDirectionIdxes[x]].OnInteract();
			//yield return true;
			yield return new WaitForSeconds(0.1f);
        }
		while (!currentSubmissionIdx.SequenceEqual(expectedSubmissionIdx))
        {
			for (var x = 0; x < expectedSubmissionIdx.Length; x++)
            {
				var idxDirDigitCur = Enumerable.Range(0, 4).Single(a => directionInputDigits[a] == expectedSubmissionIdx[x]);
				directionsSelectable[idxDirDigitCur].OnInteract();
				yield return new WaitForSeconds(0.1f);
			}
        }
		var idxsubDir = Enumerable.Range(0, 4).Single(a => directionInputDigits[a] == 3);
		directionsSelectable[idxsubDir].OnInteract();
		//yield return new WaitForSeconds(0.1f);
	}

	public class SubtractNauseamSettings
    {
		public bool storeSuccessfulResultsOnly = false;
		public int maxStoredResultsAll = -1;
    }

#pragma warning disable 414
	private readonly string BaseTwitchHelpMessage = "\"!{0} U/L/R/D\" [Presses directional button. Presses can be chained when entering the passcode.] \"!{0} prompt enable/disable/on/off/toggle\" [Enables/disables/toggles sending a message to chat for the current prompt.]";
	private string TwitchHelpMessage = "\"!{0} U/L/R/D\" [Presses directional button. Presses can be chained when entering the passcode.] \"!{0} prompt enable/disable/on/off/toggle\" [Enables/disables/toggles sending a message to chat for the current prompt.]";
#pragma warning restore 414

	private IEnumerator ProcessTwitchCommand(string command)
	{
		var matchPrompt = Regex.Match(command, @"^prompts?\s(on|off|toggle|enable|disable)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (matchPrompt.Success)
        {
			var lastPortions = matchPrompt.Value.Split().Skip(1);
			var portionSetLast = lastPortions.Single().ToUpperInvariant();
			switch (portionSetLast)
            {
				case "ON":
				case "ENABLE":
					{
						allowTPSayPrompt = true;
						yield return "sendtochat Chat Prompts have been enabled. Check the chat when you start a new attempt.";
						break;
					}
				case "OFF":
				case "DISABLE":
					{
						allowTPSayPrompt = false;
						yield return "sendtochat Chat Prompts have been disabled.";
						break;
					}
				case "TOGGLE":
                    {
						allowTPSayPrompt ^= true;
                        yield return "sendtochat Chat Prompts have been toggled" + (allowTPSayPrompt ? "on" : "off") + ".";
						break;
					}
				default:
                    {
						yield return "sendtochaterror Unknown prompt command \"" + portionSetLast + "\"";
						break;
                    }
            }
			TwitchHelpMessage = BaseTwitchHelpMessage + (allowTPSayPrompt ? " The module will send a message regarding the current prompt being enabled." : "");
			yield break;
        }
		var commandModified = command.ToUpperInvariant().Replace(" ", "");
		if (commandModified.Any(x => !"URDL".Contains(x.ToString())))
		{
			yield return "sendtochaterror Only U, L, R, and D or valid commands.";
			yield break;
		}
		if (commandModified.Length > 1 && curQuestionIdx <= 9)
		{
			yield return "sendtochaterror Only one answer to a prompt may be sent at a time until you have answered enough questions correctly.";
			yield break;
		}
		if (curQuestionIdx > 9)
		{
			int[] p = commandModified.Select(x => "URDL".IndexOf(x.ToString())).ToArray();
			for (int i = 0; i < p.Length; i++)
			{
				yield return null;
				directionsSelectable[p[i]].OnInteract();
				if (directionInputDigits[p[i]] == 3) yield break;
			}
		}
		else
		{
			yield return null;
			while (isAnimating)
				yield return "trycancel";
			directionsSelectable["URDL".IndexOf(commandModified)].OnInteract();
			//if (isAnimating)
			//	while (!hasStarted)
			//		yield return "trycancel";
			
			//yield return null;
			//if (hasStarted && allowTPSayPrompt)
			//	yield return "sendtochat " + tpMessagePrompt ?? "";
			
		}
	}
}
