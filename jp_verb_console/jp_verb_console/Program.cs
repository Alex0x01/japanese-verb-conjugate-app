using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Timers;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace jp_verb_console
{
    internal class Program
    {
        #region JSON_DATA
        public const int iMAX_ENGLISH_VERB = 6;

        // this lot is for JSON serialization, if you change it, it will probably all break.
        //=================================================================================================
        public class Verb_Data
        {
            public string desc { get; set; }        // description in english
            public string kanji { get; set; }       // kanji form
            public string kana { get; set; }        // kana form
            public string[] en { get; set; }        // list of acceptable english answers, e.g. [masen, masenn] - *note* all spaces are stripped from these and user input when comparing
        }

        public class Verb_LanguageType
        {
            public Verb_Data positive { get; set; }
            public Verb_Data negative { get; set; }
        };

        public class Verb_Form
        {
            public string name { get; set; } // e.g. "past presumprive", "present indicative" etc.
            public Verb_LanguageType casual { get; set; }
            public Verb_LanguageType formal { get; set; }
        };

        // json data is an array of these verbs
        public class Verb_Obj
        {
            public string name { get; set; }            // the name of the verb, e.g. "to go"
            public string[] class_type { get; set; }    // this is info for the verb classes, e.g. "intransitive"
            public Verb_Data te_form { get; set; }      // all verbs have one te form, this is an exception, so can't be added in "forms"
            public Verb_Form[] forms { get; set; }      // an array of the different verb conjugates, easy to add more in json. e.g. "past presumptive"
        }

        // essentially... this is all of the JSON data clumped into this
        public class Verb_Arr
        {
            public Verb_Obj[] verbs { get; set; }
        };

        // Gramar
        public class Grammar_Obj
        {
            public string[] english { get; set; }
            public string[] japanese { get; set; }
            public string[] examples { get; set; }

        };

        public class Grammar_Obj_List
        {
            public Grammar_Obj[] grammar_list { get; set; }
        };
        #endregion // JSON_DATA
        //=================================================================================================

        public enum E_GameMode
        {
            Verb_Random,           // randomly pick from everything
            Verb_All,              // do everything from top to bottom
            Verb_SingleFormat,     // pass in one format, and do that single format for each verb e.g. te form
            Verb_SingleAll,        // same as single format but loops through each format
            Verb_RepeatVerb,       // just do the same one over and over, require -repeat_verb
            Verb_ReviseMode,
            Grammar_Random,
            _E_NumGameModes
        };

        // ignoring my usual naming convention of enums for json deserialization.
        public enum E_VerbFormat
        {
            NULL = -1,
            casual_positive,    // all verb conjugates will have have this (excluding te form)
            casual_negative,    // all verb conjugates will have have this (excluding te form)
            formal_positive,    // all verb conjugates will have have this (excluding te form)
            formal_negative,    // all verb conjugates will have have this (excluding te form)
            te_form,            // te form is a weird exception so it has to be "treat" differently
            _num_formats
        }
        //=================================================================================================

        // a clump of data, with defaults, we can add args to the cmd line to set these. pass -help to see.
        public class CmdArgs
        {
            public List<int> iSingleVerbIndexes = new List<int>();
            public E_VerbFormat eSingleFormatMode = E_VerbFormat.casual_positive;
            public E_GameMode eGameMode = E_GameMode.Verb_SingleAll;
            public int iNumQuestions = 10;
            
            // ...etc, add whatever
        };

        // this blob of data is re-used for each "game tick", it is built based on the game mode and other various data.
        public class Verb_Question
        {
            public int iVerbIndex = 0;
            public int iVerbFormIndex = 0;
            public E_VerbFormat eFormatType = E_VerbFormat.NULL;

            public Verb_Question()
            {
            }

            public Verb_Question( int verbIndex, int verbFormIndex, E_VerbFormat formatType )
            {
                this.iVerbIndex = verbIndex;
                this.iVerbFormIndex = verbFormIndex;
                this.eFormatType = formatType;
            }

            public Verb_Question( Verb_Question rhs )
            {
                this.iVerbIndex = rhs.iVerbIndex;
                this.iVerbFormIndex = rhs.iVerbFormIndex;
                this.eFormatType = rhs.eFormatType;
            }
        };
        //=================================================================================================

        public class GameResult
        {
            public string strResultMsg = string.Empty;
            public bool bResult = false;
            public int iNumQuestionsCorrect = 0;
            public int iNumQuestionsIncorrect = 0;
        };

        const int iNUM_SEEDS = 3;
        public static List<Verb_Question> s_xVerbReviseList = new List<Verb_Question>();
        public static Random[] s_xRandoms = new Random[iNUM_SEEDS];
        public static Random s_xSeed = new Random();
        //=================================================================================================

        public static string StripNonAlphaNumerics( string s )
        {
            return new string( Regex.Replace( s, "[^a-zA-Z0-9]", "" ) );
        }

        #region Verbs
        public static async Task Verb_SaveReviseListAsync( Verb_Arr xVerbs, string strFileName )
        {
            string[] lines = new string[s_xVerbReviseList.Count];
            for ( int i = 0; i < s_xVerbReviseList.Count; ++i )
            {
                Verb_Question q = s_xVerbReviseList[i];
                string strForm = string.Empty;
                string strFormEng = string.Empty;
                if ( q.eFormatType == E_VerbFormat.te_form )
                {
                    strForm = xVerbs.verbs[q.iVerbIndex].te_form.kanji;
                }
                else
                {
                    switch ( q.eFormatType )
                    {
                        case E_VerbFormat.casual_positive:
                            strForm = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].casual.positive.kanji;
                            strFormEng = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].casual.positive.en[0];
                            break;
                        case E_VerbFormat.casual_negative:
                            strForm = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].casual.negative.kanji;
                            strFormEng = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].casual.negative.en[0];
                            break;
                        case E_VerbFormat.formal_positive:
                            strForm = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].formal.positive.kanji;
                            strFormEng = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].formal.positive.en[0];
                            break;
                        case E_VerbFormat.formal_negative:
                            strForm = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].formal.negative.kanji;
                            strFormEng = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].formal.negative.en[0];
                            break;
                        default:
                            break;
                    }
                }

                string strLine = string.Format( "{0},{1},{2} : {3},{4},{5}",
                    q.iVerbIndex, q.iVerbFormIndex, (int)q.eFormatType,
                    xVerbs.verbs[q.iVerbIndex].name,
                    strFormEng,
                    strForm );

                lines[i] = strLine;
            }

            await File.WriteAllLinesAsync( strFileName, lines );
        }

        public static async Task<string[]> Verb_LoadReviseListAsync( string strFileName )
        {
            Task<string[]> lines = File.ReadAllLinesAsync( strFileName );
            await lines;
            return lines.GetAwaiter().GetResult();
        }

        static public GameResult MainLoopVerbs( CmdArgs cmdArgs, Verb_Arr xVerbs )
        {
            for ( int i = 0; i < iNUM_SEEDS; ++i ) s_xRandoms[i] = new Random();
            
            // Verb_Data - kanji, kana, + possible english
            const int iNUM_POSSIBLE_ANSWERS = 2 + iMAX_ENGLISH_VERB; 

            int iMaxPossibleQuestions = xVerbs.verbs.Length; // this counts for the te-forms
            for ( int i = 0; i < xVerbs.verbs.Length; ++i )
            {
                // * times 4, because each verb has formal-positive, formal-negative, informal-positive, informal-negative
                iMaxPossibleQuestions += xVerbs.verbs[i].forms.Length * 4;
            }

            // init stuff here
            string[] answers = new string[iNUM_POSSIBLE_ANSWERS];
            for( int i = 0; i < iNUM_POSSIBLE_ANSWERS; ++i )
                answers[i] = string.Empty;

            // used in random mode, to break the loop (based on numQuestions)
            int iLoopIter = 1;
            
            // this is used for index bounds, it's cached on the first question, because of ordering
            int iCachedCurrentVerbNumTypes = 0;
            int iCachedReviseListSize = 0;
            int iReviseListIndex = 0;

            // don't repeat the questions in random mode with this
            List<Verb_Question> doneList = new List<Verb_Question>();
            List<Verb_Question> todoList = new List<Verb_Question>(); // built from revise list

            // for japanese chars
            Console.OutputEncoding = System.Text.Encoding.Unicode;
            Console.InputEncoding = System.Text.Encoding.Unicode;

            // build this up each iteration from game mode, and re-use it
            Verb_Question question = new Verb_Question();
            GameResult result = new GameResult();

            const string strQuit1 = "quitgame", strQuit2 = "exitgame";

            string strCurrentGameMode = cmdArgs.eGameMode.ToString();
            strCurrentGameMode = strCurrentGameMode.Replace( "_", " " );

            // display game mode settings (could be from cmd line)
            {
                Console.WriteLine( "Game Mode : {0}", strCurrentGameMode );
                
                if ( cmdArgs.eGameMode == E_GameMode.Verb_Random )
                    Console.WriteLine( "Num Questions: {0}", cmdArgs.iNumQuestions );
                if ( cmdArgs.eGameMode == E_GameMode.Verb_SingleFormat )
                    Console.WriteLine( "Single format: {0}", cmdArgs.eSingleFormatMode.ToString() );
                if ( cmdArgs.eGameMode == E_GameMode.Verb_RepeatVerb )
                {
                    for ( int v = 0; v < cmdArgs.iSingleVerbIndexes.Count; ++v )
                    {
                        int idx = cmdArgs.iSingleVerbIndexes[v];
                        if ( idx >= xVerbs.verbs.Length )
                        {
                            result.bResult = false;
                            result.strResultMsg = string.Format( "Error with index for single verb: {0}, should be less than: {1}.",
                                idx, xVerbs.verbs.Length );
                            return result;
                        }
                        Console.WriteLine( "Repeated verb: {0}", idx );
                    }
                }
                Console.WriteLine( "--------------------------------------------------------------------" );
                Console.WriteLine( "End game with: {0} or {1}", strQuit1, strQuit2 );
                Console.WriteLine( "--------------------------------------------------------------------\n" );
            }

            // Load the revise list
            if ( cmdArgs.eGameMode == E_GameMode.Verb_ReviseMode )
            {
                Task<string[]> task = Verb_LoadReviseListAsync( "revise_list.txt" );
                while ( !task.IsCompleted )
                {
                }

                if ( !task.IsFaulted )
                {
                    string[] lines = task.GetAwaiter().GetResult();
                    iCachedReviseListSize = lines.Length;
                    for ( int i = 0; i < lines.Length; ++i )
                    {
                        string[] spl = lines[i].Split( ":" );
                        if ( spl.Length != 2 )
                        {
                            continue;
                        }

                        string[] q = spl[0].Split( "," );
                        if ( q.Length != 3 )
                        {
                            continue;
                        }

                        int iVerbIndex = Convert.ToInt32( q[0] );
                        int iVerbFormIndex = Convert.ToInt32( q[1] );
                        int iType = Convert.ToInt32( q[2] );
                        todoList.Add( new Verb_Question( iVerbIndex, iVerbFormIndex, (E_VerbFormat)iType ) );
                    }
                }
            }

            while ( true )
            {
                // 1) calc question index based on game mode
                if ( cmdArgs.eGameMode == E_GameMode.Verb_Random )
                {
                    if ( iLoopIter > cmdArgs.iNumQuestions )
                    {
                        result.iNumQuestionsIncorrect = s_xVerbReviseList.Count;
                        result.iNumQuestionsCorrect = cmdArgs.iNumQuestions - result.iNumQuestionsIncorrect;
                        result.strResultMsg = "All Questions completed.";
                        result.bResult = true;
                        // done
                        break;
                    }

                    question.iVerbIndex = s_xRandoms[s_xSeed.Next( iNUM_SEEDS )].Next( xVerbs.verbs.Length );
                    question.eFormatType = (E_VerbFormat)s_xRandoms[s_xSeed.Next( iNUM_SEEDS )].Next( (int)E_VerbFormat._num_formats );
                    if ( question.eFormatType == E_VerbFormat.te_form )
                        question.iVerbFormIndex = -1;
                    else
                        question.iVerbFormIndex = s_xRandoms[s_xSeed.Next( iNUM_SEEDS )].Next( xVerbs.verbs[question.iVerbIndex].forms.Length );

                    // re-roll
                    int iAttempts = 2048;
                    while ( doneList.Contains( question ) )
                    {
                        if ( doneList.Count >= iMaxPossibleQuestions || ( --iAttempts <= 0 ) )
                        {
                            // we've done all of the questions, or have given up
                            doneList.Clear();
                        }

                        question.iVerbIndex = s_xRandoms[s_xSeed.Next( iNUM_SEEDS )].Next( xVerbs.verbs.Length );
                        question.eFormatType = (E_VerbFormat)s_xRandoms[s_xSeed.Next( iNUM_SEEDS )].Next( (int)E_VerbFormat._num_formats );
                        if ( question.eFormatType == E_VerbFormat.te_form )
                            question.iVerbFormIndex = -1;
                        else
                            question.iVerbFormIndex = s_xRandoms[s_xSeed.Next( iNUM_SEEDS )].Next( xVerbs.verbs[question.iVerbIndex].forms.Length );
                    }

                    // add to the done list then
                    doneList.Add( new Verb_Question( question ) ); // use implicit copy ctor, stop shitty c# using references
                }
                else if ( cmdArgs.eGameMode == E_GameMode.Verb_All )
                {
                    if ( iLoopIter == 1 )
                    {
                        question.eFormatType = E_VerbFormat.casual_positive;
                        question.iVerbFormIndex = 0;
                    }
                    else
                    {
                        if ( question.eFormatType == E_VerbFormat.te_form )
                        {
                            // reset
                            question.eFormatType = E_VerbFormat.casual_positive;
                            question.iVerbFormIndex = 0;

                            // inc the verb now
                            ++question.iVerbIndex;

                            // look for game over
                            if ( question.iVerbIndex >= xVerbs.verbs.Length )
                            {
                                result.bResult = true;
                                result.strResultMsg = "All questions completed";
                                result.iNumQuestionsIncorrect = s_xVerbReviseList.Count;
                                result.iNumQuestionsCorrect = iMaxPossibleQuestions - s_xVerbReviseList.Count;
                                // done
                                break;
                            }
                        }
                        else
                        {
                            ++question.iVerbFormIndex;
                            if ( question.iVerbFormIndex >= iCachedCurrentVerbNumTypes )
                            {
                                question.iVerbFormIndex = 0;
                                ++question.eFormatType;
                                if ( question.eFormatType >= E_VerbFormat._num_formats )
                                {
                                    // reset
                                    question.eFormatType = E_VerbFormat.casual_positive;
                                }
                            }
                        }
                    }
                }
                else if ( cmdArgs.eGameMode == E_GameMode.Verb_SingleFormat )
                {
                    question.eFormatType = cmdArgs.eSingleFormatMode;
                    bool bIncVerb = false;

                    if ( question.eFormatType == E_VerbFormat.te_form )
                    {
                        question.iVerbFormIndex = -1;
                        bIncVerb = true;
                    }
                    else
                    {
                        if ( iLoopIter > 1 )
                        {
                            ++question.iVerbFormIndex;
                            if ( question.iVerbFormIndex >= iCachedCurrentVerbNumTypes )
                            {
                                bIncVerb = true;
                                question.iVerbFormIndex = 0;
                            }
                        }
                    }

                    if ( iLoopIter > 1 && bIncVerb )
                    {
                        ++question.iVerbIndex;
                        if ( question.iVerbIndex >= xVerbs.verbs.Length )
                        {
                            result.bResult = true;
                            result.strResultMsg = "All single format questions completed";
                            result.iNumQuestionsIncorrect = s_xVerbReviseList.Count;

                            // none te form has both positive and negative so * 2
                            int iMaxSingleQuestions = question.eFormatType == E_VerbFormat.te_form
                                ? xVerbs.verbs.Length
                                : xVerbs.verbs.Length * 2;
                            result.iNumQuestionsCorrect = iMaxSingleQuestions - s_xVerbReviseList.Count;
                            // done
                            break;
                        }
                    }
                }
                else if ( cmdArgs.eGameMode == E_GameMode.Verb_SingleAll )
                {
                    if ( question.eFormatType == E_VerbFormat.NULL )
                        ++question.eFormatType;

                    bool bIncVerb = false;

                    if ( question.eFormatType == E_VerbFormat.te_form )
                    {
                        question.iVerbFormIndex = -1;
                        bIncVerb = true;
                    }
                    if ( iLoopIter > 1 )
                    {
                        ++question.iVerbFormIndex;
                        if ( question.iVerbFormIndex >= iCachedCurrentVerbNumTypes )
                        {
                            bIncVerb = true;
                            question.iVerbFormIndex = 0;
                        }
                    }

                    if ( iLoopIter > 1 && bIncVerb )
                    {
                        ++question.iVerbIndex;
                        if ( question.iVerbIndex >= xVerbs.verbs.Length )
                        {
                            question.iVerbIndex = 0;
                            ++question.eFormatType;
                            if ( question.eFormatType >= E_VerbFormat._num_formats )
                            {
                                result.bResult = true;
                                result.strResultMsg = "All questions completed";
                                result.iNumQuestionsIncorrect = s_xVerbReviseList.Count;
                                result.iNumQuestionsCorrect = iMaxPossibleQuestions - s_xVerbReviseList.Count;
                                // done
                                break;
                            }
                        }
                    }
                }
                else if ( cmdArgs.eGameMode == E_GameMode.Verb_RepeatVerb )
                {
                    if ( cmdArgs.iSingleVerbIndexes.Count == 0 )
                    {
                        result.bResult = false;
                        result.strResultMsg = "No verbs have been set to repeat";
                        return result;
                    }

                    // we can reuse iReviseListIndex
                    question.iVerbIndex = cmdArgs.iSingleVerbIndexes[iReviseListIndex];
                    
                    if ( iLoopIter == 1 )
                    {
                        question.eFormatType = E_VerbFormat.casual_positive;
                        question.iVerbFormIndex = 0;
                    }
                    else
                    {
                        bool bIsTeForm = question.eFormatType == E_VerbFormat.te_form;

                        ++question.iVerbFormIndex;
                        if ( (!bIsTeForm && question.iVerbFormIndex >= iCachedCurrentVerbNumTypes) || bIsTeForm )
                        {
                            question.iVerbFormIndex = 0;
                            ++question.eFormatType;
                        }

                        if ( question.eFormatType >= E_VerbFormat._num_formats )
                        {
                            // reset
                            question.eFormatType = E_VerbFormat.casual_positive;
                            
                            ++iReviseListIndex;
                            if ( iReviseListIndex >= cmdArgs.iSingleVerbIndexes.Count )
                            {
                                iReviseListIndex = 0;
                            }
                            question.iVerbIndex = cmdArgs.iSingleVerbIndexes[iReviseListIndex];
                        }
                    }
                }
                else if ( cmdArgs.eGameMode == E_GameMode.Verb_ReviseMode )
                {
                    if ( todoList.Count == 0 || iReviseListIndex >= todoList.Count )
                    {
                        result.bResult = true;
                        result.strResultMsg = "Nothing left in revise list";
                        result.iNumQuestionsIncorrect = s_xVerbReviseList.Count;
                        result.iNumQuestionsCorrect = iCachedReviseListSize - s_xVerbReviseList.Count;
                        break;
                    }

                    question.iVerbIndex = todoList[iReviseListIndex].iVerbIndex;
                    question.iVerbFormIndex = todoList[iReviseListIndex].iVerbFormIndex;
                    question.eFormatType = todoList[iReviseListIndex].eFormatType;
                    ++iReviseListIndex;
                }
                else
                {
                    result.bResult = false;
                    result.strResultMsg = "Error: unhandled game mode";
                    return result;
                }
                
                // shortcuts
                Verb_Obj verb = xVerbs.verbs[question.iVerbIndex];
                Verb_Form[] arrForms = verb.forms;
                int iNUM_VERB_FORMS = arrForms.Length;
                iCachedCurrentVerbNumTypes = iNUM_VERB_FORMS;

                // 2) build question strings from the data
                Verb_Data currentVerbData = null;
                string currentFormName = string.Empty;
                string currentFormDesc = "";
                if ( question.eFormatType == E_VerbFormat.te_form )
                {
                    // te form
                    currentVerbData = verb.te_form;
                    currentFormName = "[te form]";
                }
                else
                {
                    Verb_Form vf = arrForms[question.iVerbFormIndex];
                    string formName = string.Format("[{0}]", vf.name );
                    currentFormName = formName.Replace( "_", " " );
                    
                    switch ( question.eFormatType )
                    {
                        case E_VerbFormat.casual_positive: currentVerbData = vf.casual.positive; break;  
                        case E_VerbFormat.casual_negative: currentVerbData = vf.casual.negative; break;
                        case E_VerbFormat.formal_positive: currentVerbData = vf.formal.positive; break;
                        case E_VerbFormat.formal_negative: currentVerbData = vf.formal.negative; break;
                        default: break;
                    }

                    currentFormDesc = currentVerbData.desc;
                }
                
                // quick 
                if ( currentVerbData == null )
                {
                    result.bResult = false;
                    result.strResultMsg = "Error: internal error, no verb data generated.";
                    return result;
                }

                string strFmtType = question.eFormatType.ToString();

                string strVerbClasses = "[";
                for ( int i = 0; i < verb.class_type.Length; ++i )
                {
                    strVerbClasses += verb.class_type[i];
                    strVerbClasses += i < verb.class_type.Length - 1 ? "," : "";
                }
                strVerbClasses += "]";

                // 3) build question str
                string strQuestion = string.Format( "Q{0}) Verb: {1} {2} \n\"{3}\"\n{4}, {5}",
                    iLoopIter,
                    verb.name,
                    currentFormName,
                    currentFormDesc,
                    strFmtType.Replace( "_", "-" ) ,
                    strVerbClasses);

                // 4) get all possible answers
                {
                    answers[0] = currentVerbData.kanji;
                    answers[1] = currentVerbData.kana;
                    int iOffset = 1;
                    for ( int en = 0; en < currentVerbData.en.Length; ++en )
                    {
                        if ( !string.IsNullOrEmpty( currentVerbData.en[en] ) )
                        {
                            answers[++iOffset] = currentVerbData.en[en];
                        }
                    }
                }

                // 5) fetch answer
                Console.WriteLine( strQuestion );
                String strAnswer = String.Empty;
                while ( string.IsNullOrEmpty( strAnswer ) )
                {
                    strAnswer = Console.ReadLine();
                }
                
                // remove spaces from answer
                strAnswer = strAnswer.Replace( " ", "" );
               
                // check for quit
                if ( strAnswer == strQuit1 || strAnswer == strQuit2 )
                {
                    result.iNumQuestionsIncorrect = s_xVerbReviseList.Count;
                    result.iNumQuestionsCorrect = iLoopIter - s_xVerbReviseList.Count;
                    result.bResult = true;
                    result.strResultMsg = "User quit, game completed.";
                    // done
                    break;
                }

                // 6) check answer
                {
                    bool bFailed = true;
                    for ( int i = 0; i < iNUM_POSSIBLE_ANSWERS; ++i )
                    {
                        string strippedPossibleAnswer = answers[i].Replace( " ", "" );
                        if ( !string.IsNullOrEmpty( strippedPossibleAnswer ) && (strippedPossibleAnswer == strAnswer) )
                        {
                            Console.WriteLine( "Correct! {0}", answers[0] );
                            bFailed = false;
                            break;
                        }
                    }
                    if ( bFailed )
                    {
                        Console.WriteLine( "Answer is incorrect the answer is: {0} {1}", answers[2], answers[0] );
                        s_xVerbReviseList.Add( new Verb_Question( question ) );
                    }
                }

                Console.WriteLine( "" );
                
                // clear answers
                for ( int i = 0; i < iNUM_POSSIBLE_ANSWERS; ++i )
                {
                    answers[i] = string.Empty;
                }
                ++iLoopIter;
            }
            Console.WriteLine( "--------------------------------------------------------------------\n" );

            // save out all the ones you answered wrong
            if ( s_xVerbReviseList.Count > 0 )
            {
                Console.WriteLine( "Save and overwrite revision list [Y][Enter] = yes?" );
                Console.WriteLine( "--------------------------------------------------------------------" );
                ConsoleKeyInfo info = Console.ReadKey();

                if ( info.Key == ConsoleKey.Y || info.Key == ConsoleKey.Enter )
                {
                    string strFileName = "revise_list.txt";
                    Task task = Verb_SaveReviseListAsync( xVerbs, strFileName );
                    while ( !task.IsCompleted )
                    {
                    }
                    Console.WriteLine( "\n{0} \"{1}\" list was {2}.",
                        task.IsFaulted ? "Error" : "Success",
                        strFileName,
                        task.IsFaulted ? "not saved" : "saved" );
                }
            }

            return result;
        }
        #endregion // Verbs
        //=================================================================================================

        #region Grammar
        static public GameResult MainLoopGrammars( CmdArgs cmdArgs, Grammar_Obj_List xGrammars )
        {
            for ( int i = 0; i < iNUM_SEEDS; ++i ) s_xRandoms[i] = new Random();

            GameResult result = new GameResult();
            
            // for japanese chars
            Console.OutputEncoding = System.Text.Encoding.Unicode;
            Console.InputEncoding = System.Text.Encoding.Unicode;

            // consts
            const string strQuit1 = "quitgame", strQuit2 = "exitgame";
            const int iJAP_TO_ENG = 0;
            const int iENG_TO_JAP = 1;
            int iQUESTION_LIST_SIZE = xGrammars.grammar_list.Length;

            // shrink this if needed, so we don't go out of bounds
            cmdArgs.iNumQuestions = Math.Min( cmdArgs.iNumQuestions, iQUESTION_LIST_SIZE );

            // vars
            int iQuestionNumber = -1;
            int iLoopIter = 1;
            int iNumWrong = 0;
            int iQuestionOrderMode = iJAP_TO_ENG;

            List<int> doneList = new List<int>();

            // process
            while ( true )
            {
                // 1) calc question index based on game mode
                if ( cmdArgs.eGameMode == E_GameMode.Grammar_Random )
                {
                    // break, we're done
                    if ( iLoopIter > cmdArgs.iNumQuestions )
                    {
                        result.iNumQuestionsIncorrect = iNumWrong;
                        result.iNumQuestionsCorrect = cmdArgs.iNumQuestions - result.iNumQuestionsIncorrect;
                        result.strResultMsg = "All Questions completed.";
                        result.bResult = true;
                        break;
                    }

                    iQuestionNumber = s_xRandoms[s_xSeed.Next( iNUM_SEEDS )].Next( cmdArgs.iNumQuestions );

                    // re-roll
                    int iAttempts = 2048;
                    while ( doneList.Contains( iQuestionNumber ) )
                    {
                        if ( doneList.Count >= cmdArgs.iNumQuestions || ( --iAttempts <= 0 ) )
                        {
                            // we've done all of the questions, or have given up
                            doneList.Clear();
                        }

                        iQuestionNumber = s_xRandoms[s_xSeed.Next( iNUM_SEEDS )].Next( cmdArgs.iNumQuestions );
                    }

                    // add to the done list then
                    doneList.Add( iQuestionNumber );
                }
                else
                {
                    result.bResult = false;
                    result.strResultMsg = "Error: unhandled game mode";
                    return result;
                }


                // 2) build question strings from the data
                Grammar_Obj xCurrentGramObj = xGrammars.grammar_list[iQuestionNumber];
                if ( xCurrentGramObj == null )
                {
                    result.bResult = false;
                    result.strResultMsg = "Error: internal error, no verb data generated.";
                    return result;
                }

                string[] strEnglish = xCurrentGramObj.english;
                string[] strJapanese = xCurrentGramObj.japanese;
                if ( strEnglish == null || strJapanese == null || strEnglish.Length == 0 || strJapanese.Length == 0 ||
                    strEnglish[0] == null || strJapanese[0] == null )
                {
                    Console.WriteLine( "*Warning* some data was null" );
                    goto NextIter;
                }

                int iNumAnswers = iQuestionOrderMode == iENG_TO_JAP ? strJapanese.Length : strEnglish.Length;
                string[] strQuestions = iQuestionOrderMode == iENG_TO_JAP ? strEnglish : strJapanese;
                string[] strAnswers = iQuestionOrderMode == iENG_TO_JAP ? strJapanese : strEnglish;
                string[] strExamples = xCurrentGramObj.examples;

                // show any other questions
                string strQuestBuff = "\n";
                for ( int i = 0; i < strQuestions.Length; ++i )
                {
                    strQuestBuff += string.Format( "{0} {1}{2}", "-", strQuestions[i], i<strQuestions.Length-1?"\n":"" );
                }

                // 3) build question str
                string strQuestion = string.Format( "Q{0}) {1} possible answers(s): In {2}, what is: {3}",
                    iLoopIter,
                    iNumAnswers,
                    iQuestionOrderMode == iENG_TO_JAP ? "Japanese" : "English",
                    strQuestBuff);

                // 4) fetch answer
                Console.WriteLine( strQuestion );

                Console.Write( ">> " );
                string strUserAnswer = string.Empty;
                while ( string.IsNullOrEmpty( strUserAnswer ) )
                {
                    strUserAnswer = Console.ReadLine();
                }

                // remove spaces from answer
                strUserAnswer = strUserAnswer.Replace( " ", "" );
                strUserAnswer = StripNonAlphaNumerics( strUserAnswer );
                strUserAnswer = strUserAnswer.ToLower();

                // check for quit
                if ( strUserAnswer == strQuit1 || strUserAnswer == strQuit2 )
                {
                    result.iNumQuestionsIncorrect = s_xVerbReviseList.Count;
                    result.iNumQuestionsCorrect = iLoopIter - s_xVerbReviseList.Count;
                    result.bResult = true;
                    result.strResultMsg = "User quit, game completed.";
                    break;
                }

                // 6) check answer
                {
                    bool bFailed = true;
                    for ( int i = 0; i < iNumAnswers; ++i )
                    {
                        string strippedAnswer = strAnswers[i].Replace( " ", "" );
                        strippedAnswer = StripNonAlphaNumerics( strippedAnswer );
                        strippedAnswer = strippedAnswer.ToLower();

                        if ( !string.IsNullOrEmpty( strippedAnswer ) && ( strippedAnswer == strUserAnswer ) )
                        {
                            bFailed = false;
                            break;
                        }
                    }

                    // keep score
                    iNumWrong += bFailed ? 1 : 0;
                    
                    // show possible answers
                    string strBuff = string.Format( "{0} - Answers: [ ", bFailed ? "!!Incorrect!!" : "*Correct*" );
                    for ( int i = 0; i < iNumAnswers; ++i )
                    {
                        strBuff += string.Format( "\"{0}\"{1}", strAnswers[i], i == iNumAnswers - 1 ? " ]" : ", " );
                    }
                    // show any examples
                    string strExampleBuff = strExamples == null ? string.Empty : "Example(s): [ ";
                    for ( int i = 0; strExamples != null && i < strExamples.Length; ++i )
                    {
                        strExampleBuff += string.Format( "\"{0}\"{1}", strExamples[i], i == strExamples.Length - 1 ? " ]" : ", " );
                    }

                    // print out possible answers
                    Console.WriteLine( strBuff );

                    // ask for example
                    if ( !string.IsNullOrEmpty( strExampleBuff ) )
                    {
                        Console.Write( "Practise >> " );
                        Console.Read();

                        // print out examples (don't check)
                        Console.WriteLine( strExampleBuff );
                    }
                }

            NextIter:
                Console.WriteLine( "" );
                ++iLoopIter;
            }

            return result;
        }
        #endregion // Grammar
        //=================================================================================================


        public static void Main( string[] args )
        {
            CmdArgs cmdArgs = new CmdArgs();

            Console.WriteLine( "--------------------------------------------------------------------" );
            Console.WriteLine( "[Japanese Practice] pass in -help for available args" );
            Console.WriteLine( "--------------------------------------------------------------------\n" );

            Console.WriteLine( "Please use \"Lucida Console\" instead of the default font(MenuButton>Default>Font)" );
            Console.WriteLine( "--------------------------------------------------------------------\n" );

            string strFileToLoadFromArgs = string.Empty;

            // parse args
            for ( int iArg = 0; iArg < args.Length; ++iArg )
            {
                string strCmdCurrent = args[iArg].ToLower();
                switch ( strCmdCurrent )
                {
                    case "-help":
                    {
                        Console.WriteLine( "Verb conjugator help!" );
                        Console.WriteLine( "--------------------------------------------------------------------" );
                        Console.WriteLine( "The following are valid commands:" );
                        Console.WriteLine( "--------------------------------------------------------------------" );

                        // mode
                        string strModes = "[";
                        for ( int m = 0; m < (int)E_GameMode._E_NumGameModes; ++m )
                        {
                            E_GameMode e = (E_GameMode)m;
                            strModes += string.Format( "{0}={1}{2}", m, e.ToString(), m < (int)E_GameMode._E_NumGameModes-1 ? "," : "]" );
                        }
                        Console.WriteLine( "-mode [0-{0}] -- {1}", (int)E_GameMode._E_NumGameModes, strModes );

                        // single format param
                        strModes = "[";
                        for ( int m = 0; m < (int)E_VerbFormat._num_formats; ++m )
                        {
                            E_VerbFormat e = (E_VerbFormat)m;
                            strModes += string.Format( "{0}={1}{2}", m, e.ToString(), m < (int)E_VerbFormat._num_formats-1 ? "," : "]" );
                        }
                        Console.WriteLine( "-single_format [0-{0}] -- {1}", (int)E_VerbFormat._num_formats, strModes );

                        // num questions
                        Console.WriteLine( "-num_questions i = used to set the max amount of questions in Random Mode" );
                        Console.WriteLine( "-repeat_verb i = used in game mode single repeat" );
                        Console.WriteLine( "-file s = pass json file to load" );
                        Console.WriteLine( "--------------------------------------------------------------------" );
                        Console.WriteLine( "--------------------------------------------------------------------" );
                        Console.WriteLine( "\n" );
                        break;
                    }
                    case "-file":
                    {
                        int iNextArg = iArg + 1;
                        if ( iNextArg >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }
                        strFileToLoadFromArgs = args[iNextArg];
                        iArg++;
                        break;
                    }
                    case "-num_questions":
                    {
                        int iNextArg = iArg + 1;
                        if ( iNextArg >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }
                        cmdArgs.iNumQuestions = Convert.ToInt32( args[iNextArg] );
                        iArg++; 
                        break;
                    }
                    case "-repeat_verb":
                    {
                        int iNextArg = iArg + 1;
                        if ( iNextArg >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }

                        string[] strVerbList = args[iNextArg].Split( "," );
                        if ( strVerbList.Length == 0 )
                        {
                            cmdArgs.iSingleVerbIndexes.Add( 0 );
                        }
                        else
                        {
                            for ( int verb = 0; verb < strVerbList.Length; ++verb )
                            {
                                int iIndex = Convert.ToInt32( strVerbList[verb] );
                                cmdArgs.iSingleVerbIndexes.Add( iIndex );
                            }
                        }

                        iArg++;
                        break;
                    }
                    case "-mode":
                    {
                        int iNextArg = iArg + 1;
                        if ( iNextArg >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }
                        int iMode = Convert.ToInt32( args[iNextArg] );
                        cmdArgs.eGameMode = (E_GameMode)iMode;
                        iArg++;
                        break;
                    }
                    case "-single_format":
                    {
                        int nextIndex = iArg + 1;
                        if ( nextIndex >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }
                        int iMode = Convert.ToInt32( args[nextIndex] );
                        cmdArgs.eSingleFormatMode = (E_VerbFormat)iMode;
                        iArg++;
                        break;
                    }
                    default:
                    {
                        Console.WriteLine( "Unkown cmd {0}", strCmdCurrent );
                        break;
                    }
                }
            }

            string strFileName = string.IsNullOrEmpty( strFileToLoadFromArgs ) ?
                "../../../data/data_verbs_small.json" : strFileToLoadFromArgs;
            try
            {
                byte[] aJsonSrc = File.ReadAllBytes( strFileName );
                GameResult xRes = null;

                // verb game
                if ( cmdArgs.eGameMode < E_GameMode.Grammar_Random )
                {
                    // process
                    Verb_Arr xVerbs = JsonSerializer.Deserialize<Verb_Arr>( aJsonSrc );
                    xRes = MainLoopVerbs( cmdArgs, xVerbs );
                }
                // grammar
                else
                {
                    Grammar_Obj_List xGrammars = JsonSerializer.Deserialize<Grammar_Obj_List>( aJsonSrc );
                    xRes = MainLoopGrammars( cmdArgs, xGrammars );
                }

                // result
                Console.WriteLine( "Game over: {0}", xRes.bResult ? "Success" : "Failure" );
                Console.WriteLine( xRes.strResultMsg );
                if ( xRes.bResult )
                {
                    Console.WriteLine( "Questions Correct: {0}. Questions Incorrect {1}", xRes.iNumQuestionsCorrect, xRes.iNumQuestionsIncorrect );
                }

                // game over man!
                Console.WriteLine( "--------------------------------------------------------------------" );
                Console.WriteLine( "Program finished.... please press any key to exit" );
                Console.WriteLine( "--------------------------------------------------------------------" );
                Console.ReadKey();
            }
            catch ( Exception ex )
            {
                Console.WriteLine( ex.Message );
            }
        }
    }
}
