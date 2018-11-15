﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using JetBrains.Annotations;
using ModCommon.Util;
using ModCommon;
using Modding;
using UnityEngine;
using UnityEngine.UI;
using Logger = Modding.Logger;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace DoH
{
    internal class Hornet : MonoBehaviour
    {
        private readonly Dictionary<string, float> _fpsDict = new Dictionary<string, float> //Use to change animation speed
        {
            //["Run"] = 100
            ["Counter Attack 1"] = 8,
            ["Counter Attack 2"] = 8
        };
        public static HealthManager _hm;
        private tk2dSpriteAnimator _anim;
        private Recoil _recoil;
        private PlayMakerFSM _stunControl;
        private PlayMakerFSM _control;
        private PlayMakerFSM _needleControl;
        private PlayMakerFSM _needleControl2;
        private PlayMakerFSM _weaverControl;
        private PlayMakerFSM _beamControlR;
        private PlayMakerFSM _beamControlL;
        private GameObject needle;
        private GameObject needle2;
        private GameObject canvas;
        private GameObject weaver;
        private GameObject weaverPref;
        private Text textExample;
        private GameObject grubRPref;
        private GameObject grubR;
        private GameObject grubLPref;
        private GameObject grubL;
        private GameObject wave;
        private GameObject wavePref;

        float timeLeft;
        float heightNeedle1;
        float heightNeedle2;
        float needleVelocity;
        float height;
        private bool finalPhase = false;
        private bool secondPhase = false;

        private void Awake()
        {
            Log("Added Hornet Mono");

            if (!PlayerData.instance.hornetOutskirtsDefeated) return;
            if (!DoH.Instance.IsInHall) return;
            _hm = gameObject.GetComponent<HealthManager>();
            _stunControl = gameObject.LocateMyFSM("Stun Control");
            _control = gameObject.LocateMyFSM("Control");
            _recoil = gameObject.GetComponent<Recoil>();
            _anim = gameObject.GetComponent<tk2dSpriteAnimator>();
            Resources.LoadAll<GameObject>("");
            foreach (var i in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (i.name == "Weaverling")
                {
                    weaverPref = i;
                }
                else if (i.name == "Grubberfly BeamR R")
                {
                    grubRPref = i;
                }
                else if (i.name == "Grubberfly BeamL R")
                {
                    grubLPref = i;
                }
                else if (i.name == "lava_particles_03")
                {
                    wavePref = i;
                }
            }
        }

        private void Start()
        {
            if (!PlayerData.instance.hornetOutskirtsDefeated) return;
            if (!DoH.Instance.IsInHall) return;

            CanvasUtil.CreateFonts();
            canvas = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceOverlay, new Vector2(1920, 1080));
            UnityEngine.Object.DontDestroyOnLoad(canvas);
            textExample = CanvasUtil.CreateTextPanel(canvas, "", 30, TextAnchor.MiddleLeft, new CanvasUtil.RectData(new Vector2(600, 50), new Vector2(-560, 805), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f)), true).GetComponent<Text>();
            textExample.color = new Color(1f, 1f, 1f, 1f);

            timeLeft = 2f;

            // Refill MP
            HeroController.instance.AddMPChargeSpa(999);

            // No stunning
            Destroy(_stunControl);

            // 1500hp
            _hm.hp = 1500;

            // Disable Knockback
            _recoil.enabled = false;

            // 2x Damage on All Components: Enable this later...
            /*foreach (DamageHero i in gameObject.GetComponentsInChildren<DamageHero>(true))
            {
                Log(i.name);
                i.damageDealt *= 2;
            }*/

            // Speed up some attacks.
            try
            {
                foreach (KeyValuePair<string, float> i in _fpsDict)
                {
                    _anim.GetClipByName(i.Key).fps = i.Value;
                }
            }
            catch(System.Exception e)
            {
                Log(e);
            }
            

            // Stop pointless standing in place
            _control.GetAction<WaitRandom>("Idle", 9).timeMax = 0f;
            _control.GetAction<WaitRandom>("Idle", 9).timeMin = 0f;

            // Stop pointless running
            _control.GetAction<WaitRandom>("Run", 6).timeMax = 0f;
            _control.GetAction<WaitRandom>("Run", 6).timeMin = 0f;

            //Make Hornet hold her sphere for 5 seconds
            var go = _control.GetAction<ActivateGameObject>("Sphere Recover A", 1).gameObject.GameObject.Value;
            var go2 = _control.GetAction<ActivateGameObject>("Sphere Recover", 1).gameObject.GameObject.Value;
            IEnumerator ActivateSphereA()
            {
                Log("Hornet: Activate Air turbo sphere mode, DIE little Ghost. get nae-naed");
                go.SetActive(true);
                yield return new WaitForSeconds(3f);
                go.SetActive(false);
            }
            IEnumerator ActivateSphereG()
            {
                Log("Hornet: Activate Ground turbo sphere mode, DIE little Ghost.");
                go2.SetActive(true);
                yield return new WaitForSeconds(3f);
                go2.SetActive(false);
            }
            _control.CopyState("Sphere Recover", "Sphere Recover Old");
            _control.CopyState("Sphere Recover A", "Sphere Recover A Old");
            _control.RemoveAction("Sphere Recover A", 1);
            _control.RemoveAction("Sphere Recover", 1);
            _control.InsertCoroutine("Sphere Recover A", 1, ActivateSphereA);
            _control.InsertCoroutine("Sphere Recover", 1, ActivateSphereG);

            //Make Hornet Dash after needle throw
            var removeNeedle = _control.GetAction<ActivateGameObject>("Throw Recover", 0);
            _control.InsertAction("Throw Antic", removeNeedle, 0);
            removeNeedle.gameObject.GameObject.Value.LocateMyFSM("Control").ChangeTransition("Out", "FINISHED", "Notify");
            _control.CopyState("Jump", "Jump2");
            _control.CopyState("ADash Antic", "ADash Antic 2");
            _control.GetAction<GetAngleToTarget2D>("ADash Antic 2", 1).offsetY.Value += 3f;
            _control.ChangeTransition("Throw", "FINISHED", "Jump2");
            _control.ChangeTransition("Jump2", "FINISHED", "ADash Antic 2");

            //Make a better G Dash for attacking you while you heal
            var lookAtKnight = _control.GetAction<FaceObject>("GDash Antic", 2);
            _control.CopyState("G Dash", "G Dash 2");
            _control.InsertAction("G Dash 2", new FaceObject
            {
                objectA = lookAtKnight.objectA,
                objectB = lookAtKnight.objectB,
                spriteFacesRight = lookAtKnight.spriteFacesRight,
                playNewAnimation = lookAtKnight.playNewAnimation,
                newAnimationClip = lookAtKnight.newAnimationClip,
                resetFrame = lookAtKnight.resetFrame,
                everyFrame = lookAtKnight.everyFrame
            }, 0);
            _control.ChangeTransition("G Dash 2", "FINISHED", "CA Antic");

            try
            {
                Log("Create a copy of the needle and use it as a horizontal attack");
                needle = Instantiate(_control.GetAction<SetPosition>("Throw", 4).gameObject.GameObject.Value);
                _needleControl = needle.LocateMyFSM("Control");
                _needleControl.ChangeTransition("Out", "FINISHED", "Notify");
                needle.SetActive(true);
                this.needle.GetComponent<Rigidbody2D>().velocity = new Vector2(0f, 0f);
                needle.transform.SetPosition2D(12, 35);
                this.needle.AddComponent<TinkEffect>();
                Hornet.Log("Error was here right?");
                UnityEngine.Object.Destroy(this.needle.GetComponent<NonBouncer>());
                var tink = UnityEngine.Object.Instantiate(GameObject.Find("Needle Tink")).AddComponent<ModCommon.NeedleTink>();
                tink.SetParent(needle.transform);
                heightNeedle1 = 30;
                heightNeedle2 = 35;
                needleVelocity = 15;

                Log("Create a copy of the needle and use it as a horizontal attack");
                needle2 = Instantiate(_control.GetAction<SetPosition>("Throw", 4).gameObject.GameObject.Value);
                _needleControl2 = needle2.LocateMyFSM("Control");
                _needleControl2.ChangeTransition("Out", "FINISHED", "Notify");
                needle2.SetActive(true);
                this.needle2.GetComponent<Rigidbody2D>().velocity = new Vector2(0f, 0f);
                needle2.transform.SetPosition2D(40, 35);
                this.needle2.AddComponent<TinkEffect>();
                Hornet.Log("Error was here right?");
                UnityEngine.Object.Destroy(this.needle2.GetComponent<NonBouncer>());
                var tink2 = UnityEngine.Object.Instantiate(GameObject.Find("Needle Tink")).AddComponent<ModCommon.NeedleTink>();
                tink2.SetParent(needle2.transform);

                Log("Remove Evade when hit because it's dumb and also makes it so when hit hornet has a higher chance of either attacking or jumping");
                _control.GetAction<SendRandomEvent>("Dmg Response", 0).weights[0] = 0f;
                _control.GetAction<SendRandomEvent>("Dmg Response", 0).weights[1] = 0.4f;
                _control.GetAction<SendRandomEvent>("Dmg Response", 0).weights[2] = 0.5f;
                _control.GetAction<SendRandomEvent>("Dmg Response", 0).weights[3] = 0.1f;


                Log("When she gets hit she does not only jump");
                _control.ChangeTransition("Dmg Response", "JUMP", "Jump Antic");

                Log("Skip waiting for player to hit her counter and never do the dumb evade move");
                _control.GetAction<Wait>("Counter Stance", 1).time = 0f;
                _control.ChangeTransition("Counter Stance", "FINISHED", "CA 1");

                Log("Choose Counter over Evade");
                _control.GetAction<SendRandomEvent>("Ev Or Counter", 0).weights[0] = 0f;
                _control.GetAction<SendRandomEvent>("Ev Or Counter", 0).weights[1] = 1f;

                Log("Choose GDash over Evade");
                _control.ChangeTransition("Run", "EVADE", "GDash Antic");
            }
            catch (System.Exception e)
            {
                Log(e);
            }

            //Removing useless barbs
            _control.ChangeTransition("Barb?", "BARB", "Can Throw?");
            

            Log("Added health recovery with Weavers.");
            weaver = Instantiate(weaverPref);
            weaver.transform.SetPosition2D(gameObject.transform.GetPositionX(), gameObject.transform.GetPositionY());
            var warpDelete = weaver.LocateMyFSM("Warp To Hero");
            _weaverControl = weaver.LocateMyFSM("Control");
            warpDelete.ChangeTransition("Check", "WARP", "Idle");
            weaver.AddComponent<HealthManager>().hp = 1;
            weaver.AddComponent<DamageEnemies>().damageDealt = 0;
            weaver.AddComponent<WeaverScript>();
            weaver.SetActive(false);

            Log("fin.");
        }
        public void grubberAttack()
        {
      
            if (HeroController.instance.gameObject.transform.GetPositionX() - gameObject.transform.GetPositionX() > 0)
            {
                grubR = Instantiate(grubRPref);
                _beamControlR = grubR.LocateMyFSM("Control");
                _beamControlR.GetAction<Wait>("Active", 0).time = 5f;
                Destroy(grubR.LocateMyFSM("damagesenemy"));
                grubR.transform.SetPosition2D(gameObject.transform.GetPositionX(), gameObject.transform.GetPositionY());
                grubR.AddComponent<DamageHero>();
                grubR.GetComponent<DamageHero>().damageDealt *= 2;
                grubR.SetActive(true);
            }
            else
            {
                grubL = Instantiate(grubLPref);
                _beamControlL = grubL.LocateMyFSM("Control");
                _beamControlL.GetAction<Wait>("Active", 0).time = 5f;
                Destroy(grubL.LocateMyFSM("damagesenemy"));
                grubL.transform.SetPosition2D(gameObject.transform.GetPositionX(), gameObject.transform.GetPositionY());
                grubL.AddComponent<DamageHero>();
                grubL.GetComponent<DamageHero>().damageDealt *= 2;
                grubL.SetActive(true);
            }
            
        }
        public void grubberAttack2()
        {
            if (HeroController.instance.gameObject.transform.GetPositionX() - gameObject.transform.GetPositionX() > 0)
            {
                grubR = Instantiate(grubRPref);
                _beamControlR = grubR.LocateMyFSM("Control");
                _beamControlR.GetAction<Wait>("Active", 0).time = 5f;
                Destroy(grubR.LocateMyFSM("damagesenemy"));
                grubR.transform.SetPosition2D(gameObject.transform.GetPositionX(), HeroController.instance.gameObject.transform.GetPositionY());
                grubR.AddComponent<DamageHero>();
                grubR.GetComponent<DamageHero>().damageDealt *= 2;
                grubR.SetActive(true);
            }
            else
            {
                grubL = Instantiate(grubLPref);
                _beamControlL = grubL.LocateMyFSM("Control");
                _beamControlL.GetAction<Wait>("Active", 0).time = 5f;
                Destroy(grubL.LocateMyFSM("damagesenemy"));
                grubL.transform.SetPosition2D(gameObject.transform.GetPositionX(), HeroController.instance.gameObject.transform.GetPositionY());
                grubL.AddComponent<DamageHero>();
                grubL.GetComponent<DamageHero>().damageDealt *= 2;
                grubL.SetActive(true);
            }
            
        }
        private void Update()
        {
            if (_hm.hp <= 1000)
            {
                var wX = weaver.transform.GetPositionX();
                var wY = weaver.transform.GetPositionY();
                var hX = gameObject.transform.GetPositionX();
                var hY = gameObject.transform.GetPositionY();
                if (!secondPhase)
                {
                    needle.SetActive(false);
                    needle2.SetActive(false);
                    Log("Do da grubber throw boiu");
                    _control.InsertMethod("CA Recover", 0, grubberAttack);
                    _control.ChangeTransition("Move Choice B", "G DASH", "CA 1");

                    IEnumerator GrubBoiThrow()
                    {
                        yield return new WaitForSeconds(0.2f);
                        grubberAttack2();
                        yield return new WaitForSeconds(0.2f);
                        grubberAttack2();
                        yield return new WaitForSeconds(0.2f);
                        grubberAttack2();
                    }
                    _control.InsertCoroutine("Jump2", 0, GrubBoiThrow);
                    Log("Add Weaver Boios");
                    _control.InsertMethod("Sphere", 0, createWeaver);
                    _control.InsertMethod("Sphere A", 0, createWeaver);
                    textExample.text = "Mother forgive my inaction.";
                    secondPhase = true;
                }
                if (Mathf.Abs(wX-hX) >= 0 && Mathf.Abs(wX - hX) <= 1 && Mathf.Abs(wY - hY) >= 0 && Mathf.Abs(wY - hY) <= 1 && _hm.hp <= 1400)
                {
                    _hm.hp += 1;
                }
            }
            if (_hm.hp <= 500)
            {
                if (!finalPhase)
                {
                    //Start lava particle
                    wave = Instantiate(wavePref);
                    wave.SetActive(true);
                    //Remove extended sphere
                    _control.RemoveAction("Sphere Recover A", 1);
                    _control.RemoveAction("Sphere Recover", 1);
                    _control.InsertAction("Sphere Recover A", _control.GetAction<ActivateGameObject>("Sphere Recover A Old", 1), 1);
                    _control.InsertAction("Sphere Recover", _control.GetAction<ActivateGameObject>("Sphere Recover Old", 1), 1);
                    //Text
                    textExample.text = "I can't let you win ghost.";
                    //Make her spam air dash
                    _control.ChangeTransition("In Air", "LAND", "ADash Antic");
                    finalPhase = true;
                }
                //Make lava particle follow her
                wave.transform.SetPosition2D(gameObject.transform.GetPositionX(), gameObject.transform.GetPositionY());
            }
            if (needle.transform.GetPositionX() <= 12)
            {
                needle.transform.SetPosition2D(12, heightNeedle1);
                needle.GetComponent<Rigidbody2D>().rotation = 180;
                needle.GetComponent<Rigidbody2D>().velocity = new Vector2(needleVelocity, 0);
            }
            else if (needle.transform.GetPositionX() >= 40)
            {
                needle.GetComponent<Rigidbody2D>().rotation = 0;
                needle.GetComponent<Rigidbody2D>().velocity = new Vector2(-1 * needleVelocity, 0);
            }
            if (needle2.transform.GetPositionX() <= 12)
            {
                needle2.transform.SetPosition2D(12, heightNeedle2);
                needle2.GetComponent<Rigidbody2D>().rotation = 180;
                needle2.GetComponent<Rigidbody2D>().velocity = new Vector2(needleVelocity, 0);
            }
            else if (needle2.transform.GetPositionX() >= 40)
            {
                needle2.GetComponent<Rigidbody2D>().rotation = 0;
                needle2.GetComponent<Rigidbody2D>().velocity = new Vector2(-1 * needleVelocity, 0);
            }

            if (HeroController.instance.cState.focusing && _control.transform.GetPositionY() < 29)
            {
                textExample.text = "Healing is for the weak ghost";
                _control.SetState("G Dash 2");
            }
            else if (HeroController.instance.cState.focusing && _control.transform.GetPositionY() > 29)
            {
                //Cant get her to attack the knight properly. Maybe have needle hit knight?
            }

            if (!textExample.text.Equals(""))
            {
                timeLeft -= Time.deltaTime;
            }
            if (timeLeft <= 0f)
            {
                textExample.text = "";
                timeLeft = 2f;
            }
        }

        private void OnDestroy()
        {
            Destroy(canvas);
        }
        public void createWeaver()
        {
            Log("Getting weavers to spawn");
            if (!weaver.activeSelf)
            {
                weaver.transform.SetPosition2D(gameObject.transform.GetPositionX(), gameObject.transform.GetPositionY());
                weaver.SetActive(true);
            }
        }
        private static void Log(object obj)
        {
            Logger.Log("[Daughter of Hallownest] " + obj);
        }
    }
}