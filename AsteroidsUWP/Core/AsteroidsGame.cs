﻿using System.Collections.Generic;
using AsteroidsUWP.GameObjects;
using AsteroidsUWP.GameObjects.Meteors;
using System;
using System.Numerics;
using Windows.UI;
using Microsoft.Graphics.Canvas;

namespace AsteroidsUWP.Core
{
    public class AsteroidsGame : Game
    {
        private EnemyShipShooterService _enemyShipShooterService;
        private readonly PlayerShip _ship;
        private Photon[] _photons;
        private IGameController _gameController;
        private List<Meteor> _meteors;
        private GameManager _gameManager;
        private readonly GameScoreKeeper _gameScoreKeeper;
        private readonly EnemyShip _enemyShip;
        private readonly PhotonTimeManager _shipPhotonTimeManager = new PhotonTimeManager(150);
        private readonly ScoredPointsDisplay _scorePointsDisplay = new ScoredPointsDisplay();
        private DateTime _enemyShipLastShown;

        public AsteroidsGame(IGameWindow parent, IGameController gameController) : base(parent)
        {
            _gameScoreKeeper = new GameScoreKeeper(parent);
            _gameController = gameController;
            _ship = new PlayerShip(parent);
            _enemyShip = new EnemyShip(parent);
            StartNewRound();
        }

        private void StartNewRound()
        {
            _enemyShipLastShown = DateTime.Now;
            _gameScoreKeeper.IncrementLevel();
            _photons = new Photon[20];
            _meteors = new List<Meteor>();

            _enemyShipShooterService = new EnemyShipShooterService(_enemyShip, ParentWindow);

            for(int i = 0; i < _photons.Length; i++)
            {
                _photons[i] = new Photon(ParentWindow);
            }

            _photons[0] = new TracerPhoton(ParentWindow);

            for (int i = 0; i < 4+ _gameScoreKeeper.Level; i++)
            {
                _meteors.Add( new Meteor(new LargeMeteorType(), new Vector2(0, 0), ParentWindow));
            }
        }

        //TODO Get rid of event driven design
        protected void GameManager_MeteorWasHit(object sender, MeteorHitEventArgs e)
        {
            _gameScoreKeeper.UpdateScore(e.Meteor.Score);
            _scorePointsDisplay.Display(e.Meteor.Location, e.Meteor.Score);

            if (!(e.Meteor.GetNextSmallerMeteor() is NullMeteorType))
            {
                _meteors.Add(new Meteor(e.Meteor.GetNextSmallerMeteor(), e.Meteor.Location, ParentWindow));
                _meteors.Add(new Meteor(e.Meteor.GetNextSmallerMeteor(), e.Meteor.Location, ParentWindow));
            }
        }

        public override void Update()
        {
            InitializeTheGameManager();
            HandleControllerInputs();
            UpdateTheShip();
            UpdatePhotons();
            UpdateMeteors();
            DetectBulletCollisions();
            DetectShipToMeteorCollisions();
            StartRoundOverIfNecessary();
            ReactivateAShipIfPlayerHasLivesLeft();
            SetGameOverIfNecessary();
            SendOffEnemyShip();
            _enemyShip.Update();
            _enemyShipShooterService.Update();
            _enemyShipShooterService.FireRound();
            _scorePointsDisplay.Update();
        }

        private void SendOffEnemyShip()
        {
            if (!_enemyShip.IsActive && DateTime.Now.Subtract(_enemyShipLastShown).TotalSeconds > Constants.EnemyShipInterval)
            {
                _enemyShip.Activate();
                _enemyShipLastShown = DateTime.Now;
            }
        }

        private void SetGameOverIfNecessary()
        {
            if (_gameScoreKeeper.Lives == 0)
            {
                _gameScoreKeeper.SetStandaloneMode();
            }
        }

        private void InitializeTheGameManager()
        {
            if (_gameManager == null)
            {
                _gameManager = GameManager.TheGameManager;
                _gameManager.MeteorWasHit += GameManager_MeteorWasHit;
            }
        }

        private void UpdateTheShip()
        {
            _ship.Update();
        }

        private void HandleControllerInputs()
        {
            if (_gameController.RadialControllerState.RotationDelta != 0)
            {
                _ship.Rotate(_gameController.RadialControllerState.RotationDelta);
                _gameController.RadialControllerState.RotationDelta = 0;
            }

            if (_gameController.RadialControllerState.IsButtonPressed)
            {
                if(_gameScoreKeeper.Lives == 0)
                    Init(_gameController);
                else
                    Fire();
                _gameController.RadialControllerState.IsButtonPressed = false;
            }

            if(_gameController.KeyboardState.IsLeftKeyDown)
                _ship.RotateLeft();

            if(_gameController.KeyboardState.IsRightKeyDown)
                _ship.RotateRight();
            
            if(_gameController.KeyboardState.IsUpKeyDown)
                _ship.Thrust();
            else if(_gameController.KeyboardState.IsDownKeyDown)
                _ship.SlowDown();
            
            if (_gameController.KeyboardState.IsFireKeyDown)
                Fire();
            
            if (_gameController.KeyboardState.IsShieldKeyDown)
                _ship.TurnOnShield();

            if (_gameController.KeyboardState.IsUpKeyDown)
                _ship.TurnOnThruster();
            else
                _ship.TurnOffThruster();
        }

        private void ReactivateAShipIfPlayerHasLivesLeft()
        {
            if (!_ship.IsActive && _gameScoreKeeper.Lives > 0)
            {
                _ship.Activate();
            }
        }

        private void StartRoundOverIfNecessary()
        {
            if(RoundIsComplete())
                StartNewRound();
        }

        private bool RoundIsComplete()
        {
            bool thereAreNoMoreActiveMeteors = true;

            foreach (Meteor meteor in _meteors)
            {
                if (meteor.IsActive)
                    thereAreNoMoreActiveMeteors = false;
            }

            return thereAreNoMoreActiveMeteors;
        }

        private void DetectBulletCollisions()
        {
            for(int meteorIndex = 0; meteorIndex < _meteors.Count; meteorIndex++)
            {
                for (int bulletIndex = 0; bulletIndex < _photons.Length; bulletIndex++)
                {
                    //TODO get rid of event drive desing here
                    _meteors[meteorIndex].TestIfShot(_photons[bulletIndex]);
                }
            }

            //Test if player ship has been hit by the enemy ships bullet
            if (_enemyShipShooterService.PhotonCollidesWithShip(_ship))
            {
                BlowUpShip();
            }

            //Test to see if the players bullet has hit the enemy ship
            foreach (Photon photon in _photons)
            {
                if (photon.IsActive && _enemyShip.IsActive)
                {
                    if (_enemyShip.IsPointWithin(photon.Location))
                    {
                        _enemyShip.Inactivate();
                        _gameScoreKeeper.UpdateScore(2000);
                    }
                }
            }
        }

        private void DetectShipToMeteorCollisions()
        {
            if (!_ship.IsActive)
                return;

            for (int meteorIndex = 0; meteorIndex < _meteors.Count; meteorIndex++)
            {
                if (_meteors[meteorIndex].CollidesWithShip(_ship))
                {
                    _meteors[meteorIndex].IsActive = false;
                    GameManager.RaiseMeteorHit(_meteors[meteorIndex]);
                    BlowUpShip();
                }
            }
        }

        private void DetectShipToShipCollision()
        {
            if (_enemyShip.IsPointWithin(_ship.Location))
            {
                BlowUpShip();
            }
        }

        private void BlowUpShip()
        {
            _ship.SetInactive();
            _gameScoreKeeper.DecrementNumberOfLives();
            _ship.BlowUpShip();
        }
                   
        private void UpdatePhotons()
        {
            _photons.Update();
        }

        private void UpdateMeteors()
        {
            _meteors.Update();
        }

        private void DrawPhotons(CanvasDrawingSession graphics)
        {
            _photons.Draw(graphics);
        }

        private void DrawMeteors(CanvasDrawingSession graphics)
        {
            _meteors.Draw(graphics);
        }

        private void Fire()
        {
            if (!_ship.IsActive)
                return;

            foreach (var photon in _photons)
            {
                if (!photon.IsActive)
                {
                    photon.Fire(_ship.Location, _ship.ShipDirection, MaxPlayerBulletDistance, _shipPhotonTimeManager);
                    break;
                }
            }
        }

        public float MaxPlayerBulletDistance
        {
            get { return Math.Max(ParentWindow.WindowWidth, ParentWindow.WindowHeight)*0.5f; }
        }

        public override void Draw(CanvasDrawingSession graphics)
        {
            ClearTheCanvas(graphics);
            DrawShips(graphics);
            DrawPhotons(graphics);
            DrawMeteors(graphics);
            DrawHeadsUpDisplay(graphics);
            _enemyShipShooterService.Draw(graphics);
            _scorePointsDisplay.Draw(graphics);
        }

        private void ClearTheCanvas(CanvasDrawingSession graphics)
        {
            graphics.FillRectangle(0, 0, ParentWindow.WindowWidth, ParentWindow.WindowHeight, Colors.Black);
            //graphics.FillRectangle(Brushes.Black, 0, 0, Constants.CanvasWidth, Constants.CanvasWidth);
        }

        private void DrawHeadsUpDisplay(CanvasDrawingSession graphics)
        {
            _gameScoreKeeper.Draw(graphics);
        }

        private void DrawShips(CanvasDrawingSession graphics)
        {
            _ship.Draw(graphics);
            _enemyShip.Draw(graphics);
        }

        public void Init(IGameController controller)
        {
            _gameController = controller;
            _gameScoreKeeper.StartGame();
            _ship.Activate();
            StartNewRound();
        }

        public void CreateResources(ICanvasResourceCreator resourceCreator)
        {
            _ship.CreateResources(resourceCreator);
        }
    }
}