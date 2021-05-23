﻿/*
* Velcro Physics:
* Copyright (c) 2017 Ian Qvist
* 
* Original source Box2D:
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org 
* 
* This software is provided 'as-is', without any express or implied 
* warranty.  In no event will the authors be held liable for any damages 
* arising from the use of this software. 
* Permission is granted to anyone to use this software for any purpose, 
* including commercial applications, and to alter it and redistribute it 
* freely, subject to the following restrictions: 
* 1. The origin of this software must not be misrepresented; you must not 
* claim that you wrote the original software. If you use this software 
* in a product, an acknowledgment in the product documentation would be 
* appreciated but is not required. 
* 2. Altered source versions must be plainly marked as such, and must not be 
* misrepresented as being the original software. 
* 3. This notice may not be removed or altered from any source distribution. 
*/

using System;
using System.Diagnostics;
using Genbox.VelcroPhysics.Collision.ContactSystem;
using Genbox.VelcroPhysics.Dynamics.Joints;
using Genbox.VelcroPhysics.Utilities;
using Microsoft.Xna.Framework;

namespace Genbox.VelcroPhysics.Dynamics.Solver
{
    /// <summary>This is an internal class.</summary>
    internal class Island
    {
        private float _linTolSqr = Settings.LinearSleepTolerance * Settings.LinearSleepTolerance;
        private float _angTolSqr = Settings.AngularSleepTolerance * Settings.AngularSleepTolerance;
        private ContactManager _contactManager;
        private ContactSolver _contactSolver = new ContactSolver();
        private Stopwatch _watch = new Stopwatch();

        private Contact[] _contacts;
        private Joint[] _joints;
        internal Body[] _bodies;
        internal int _bodyCount;
        internal int _bodyCapacity;
        internal int _contactCapacity;
        internal int _contactCount;

        private int _jointCapacity;
        private int _jointCount;
        private Position[] _positions;
        private Velocity[] _velocities;

        public void Reset(int bodyCapacity, int contactCapacity, int jointCapacity, ContactManager contactManager)
        {
            _bodyCapacity = bodyCapacity;
            _contactCapacity = contactCapacity;
            _jointCapacity = jointCapacity;
            _bodyCount = 0;
            _contactCount = 0;
            _jointCount = 0;

            _contactManager = contactManager;

            if (_bodies == null || _bodies.Length < bodyCapacity)
            {
                _bodies = new Body[bodyCapacity];
                _velocities = new Velocity[bodyCapacity];
                _positions = new Position[bodyCapacity];
            }

            if (_contacts == null || _contacts.Length < contactCapacity)
                _contacts = new Contact[contactCapacity * 2];

            if (_joints == null || _joints.Length < jointCapacity)
                _joints = new Joint[jointCapacity * 2];
        }

        public void Clear()
        {
            _bodyCount = 0;
            _contactCount = 0;
            _jointCount = 0;
        }

        public void Solve(ref TimeStep step, ref Vector2 gravity)
        {
            float h = step.DeltaTime;

            // Integrate velocities and apply damping. Initialize the body state.
            for (int i = 0; i < _bodyCount; ++i)
            {
                Body b = _bodies[i];

                Vector2 c = b._sweep.C;
                float a = b._sweep.A;
                Vector2 v = b._linearVelocity;
                float w = b._angularVelocity;

                // Store positions for continuous collision.
                b._sweep.C0 = b._sweep.C;
                b._sweep.A0 = b._sweep.A;

                if (b.BodyType == BodyType.Dynamic)
                {
                    //Velcro: Only apply gravity if the body wants it.
                    // Integrate velocities.
                    if (b.IgnoreGravity)
                        v += h * b._invMass * b.Mass * b._force;
                    else
                        v += h * b._invMass * (b.GravityScale * b.Mass * gravity + b._force);

                    w += h * b._invI * b._torque;

                    // Apply damping.
                    // ODE: dv/dt + c * v = 0
                    // Solution: v(t) = v0 * exp(-c * t)
                    // Time step: v(t + dt) = v0 * exp(-c * (t + dt)) = v0 * exp(-c * t) * exp(-c * dt) = v * exp(-c * dt)
                    // v2 = exp(-c * dt) * v1
                    // Taylor expansion:
                    // v2 = (1.0f - c * dt) * v1
                    v *= MathUtils.Clamp(1.0f - h * b.LinearDamping, 0.0f, 1.0f);
                    w *= MathUtils.Clamp(1.0f - h * b.AngularDamping, 0.0f, 1.0f);
                }

                _positions[i].C = c;
                _positions[i].A = a;
                _velocities[i].V = v;
                _velocities[i].W = w;
            }

            // Solver data
            SolverData solverData = new SolverData();
            solverData.Step = step;
            solverData.Positions = _positions;
            solverData.Velocities = _velocities;

            _contactSolver.Reset(step, _contactCount, _contacts, _positions, _velocities);
            _contactSolver.InitializeVelocityConstraints();

            if (Settings.EnableWarmStarting)
                _contactSolver.WarmStart();

            if (Settings.EnableDiagnostics)
                _watch.Start();

            for (int i = 0; i < _jointCount; ++i)
            {
                if (_joints[i]._enabled)
                    _joints[i].InitVelocityConstraints(ref solverData);
            }

            if (Settings.EnableDiagnostics)
                _watch.Stop();

            // Solve velocity constraints.
            for (int i = 0; i < Settings.VelocityIterations; ++i)
            {
                for (int j = 0; j < _jointCount; ++j)
                {
                    Joint joint = _joints[j];

                    if (!joint._enabled)
                        continue;

                    if (Settings.EnableDiagnostics)
                        _watch.Start();

                    joint.SolveVelocityConstraints(ref solverData);
                    joint.Validate(step.InvertedDeltaTime);

                    if (Settings.EnableDiagnostics)
                        _watch.Stop();
                }

                _contactSolver.SolveVelocityConstraints();
            }

            // Store impulses for warm starting.
            _contactSolver.StoreImpulses();

            // Integrate positions
            for (int i = 0; i < _bodyCount; ++i)
            {
                Vector2 c = _positions[i].C;
                float a = _positions[i].A;
                Vector2 v = _velocities[i].V;
                float w = _velocities[i].W;

                // Check for large velocities
                Vector2 translation = h * v;
                if (Vector2.Dot(translation, translation) > Settings.MaxTranslation * Settings.MaxTranslation)
                {
                    float ratio = Settings.MaxTranslation / translation.Length();
                    v *= ratio;
                }

                float rotation = h * w;
                if (rotation * rotation > Settings.MaxRotation * Settings.MaxRotation)
                {
                    float ratio = Settings.MaxRotation / Math.Abs(rotation);
                    w *= ratio;
                }

                // Integrate
                c += h * v;
                a += h * w;

                _positions[i].C = c;
                _positions[i].A = a;
                _velocities[i].V = v;
                _velocities[i].W = w;
            }

            // Solve position constraints
            bool positionSolved = false;
            for (int i = 0; i < Settings.PositionIterations; ++i)
            {
                bool contactsOkay = _contactSolver.SolvePositionConstraints();

                bool jointsOkay = true;
                for (int j = 0; j < _jointCount; ++j)
                {
                    Joint joint = _joints[j];

                    if (!joint._enabled)
                        continue;

                    if (Settings.EnableDiagnostics)
                        _watch.Start();

                    bool jointOkay = joint.SolvePositionConstraints(ref solverData);

                    if (Settings.EnableDiagnostics)
                        _watch.Stop();

                    jointsOkay = jointsOkay && jointOkay;
                }

                if (contactsOkay && jointsOkay)
                {
                    // Exit early if the position errors are small.
                    positionSolved = true;
                    break;
                }
            }

            if (Settings.EnableDiagnostics)
            {
                InternalTimings.JointUpdateTime = _watch.ElapsedTicks;
                _watch.Reset();
            }

            // Copy state buffers back to the bodies
            for (int i = 0; i < _bodyCount; ++i)
            {
                Body body = _bodies[i];
                body._sweep.C = _positions[i].C;
                body._sweep.A = _positions[i].A;
                body._linearVelocity = _velocities[i].V;
                body._angularVelocity = _velocities[i].W;
                body.SynchronizeTransform();
            }

            Report(_contactSolver.VelocityConstraints);

            if (Settings.AllowSleep)
            {
                float minSleepTime = MathConstants.MaxFloat;

                for (int i = 0; i < _bodyCount; ++i)
                {
                    Body b = _bodies[i];

                    if (b.BodyType == BodyType.Static)
                        continue;

                    if (!b.SleepingAllowed || b._angularVelocity * b._angularVelocity > _angTolSqr || Vector2.Dot(b._linearVelocity, b._linearVelocity) > _linTolSqr)
                    {
                        b.SleepTime = 0.0f;
                        minSleepTime = 0.0f;
                    }
                    else
                    {
                        b.SleepTime += h;
                        minSleepTime = Math.Min(minSleepTime, b.SleepTime);
                    }
                }

                if (minSleepTime >= Settings.TimeToSleep && positionSolved)
                {
                    for (int i = 0; i < _bodyCount; ++i)
                    {
                        Body b = _bodies[i];
                        b.Awake = false;
                    }
                }
            }
        }

        internal void SolveTOI(ref TimeStep subStep, int toiIndexA, int toiIndexB)
        {
            Debug.Assert(toiIndexA < _bodyCount);
            Debug.Assert(toiIndexB < _bodyCount);

            // Initialize the body state.
            for (int i = 0; i < _bodyCount; ++i)
            {
                Body b = _bodies[i];
                _positions[i].C = b._sweep.C;
                _positions[i].A = b._sweep.A;
                _velocities[i].V = b._linearVelocity;
                _velocities[i].W = b._angularVelocity;
            }

            _contactSolver.Reset(subStep, _contactCount, _contacts, _positions, _velocities);

            // Solve position constraints.
            for (int i = 0; i < Settings.TOIPositionIterations; ++i)
            {
                bool contactsOkay = _contactSolver.SolveTOIPositionConstraints(toiIndexA, toiIndexB);
                if (contactsOkay)
                    break;
            }

            // Leap of faith to new safe state.
            _bodies[toiIndexA]._sweep.C0 = _positions[toiIndexA].C;
            _bodies[toiIndexA]._sweep.A0 = _positions[toiIndexA].A;
            _bodies[toiIndexB]._sweep.C0 = _positions[toiIndexB].C;
            _bodies[toiIndexB]._sweep.A0 = _positions[toiIndexB].A;

            // No warm starting is needed for TOI events because warm
            // starting impulses were applied in the discrete solver.
            _contactSolver.InitializeVelocityConstraints();

            // Solve velocity constraints.
            for (int i = 0; i < Settings.TOIVelocityIterations; ++i)
            {
                _contactSolver.SolveVelocityConstraints();
            }

            // Don't store the TOI contact forces for warm starting
            // because they can be quite large.

            float h = subStep.DeltaTime;

            // Integrate positions.
            for (int i = 0; i < _bodyCount; ++i)
            {
                Vector2 c = _positions[i].C;
                float a = _positions[i].A;
                Vector2 v = _velocities[i].V;
                float w = _velocities[i].W;

                // Check for large velocities
                Vector2 translation = h * v;
                if (Vector2.Dot(translation, translation) > Settings.MaxTranslation * Settings.MaxTranslation)
                {
                    float ratio = Settings.MaxTranslation / translation.Length();
                    v *= ratio;
                }

                float rotation = h * w;
                if (rotation * rotation > Settings.MaxRotation * Settings.MaxRotation)
                {
                    float ratio = Settings.MaxRotation / Math.Abs(rotation);
                    w *= ratio;
                }

                // Integrate
                c += h * v;
                a += h * w;

                _positions[i].C = c;
                _positions[i].A = a;
                _velocities[i].V = v;
                _velocities[i].W = w;

                // Sync bodies
                Body body = _bodies[i];
                body._sweep.C = c;
                body._sweep.A = a;
                body._linearVelocity = v;
                body._angularVelocity = w;
                body.SynchronizeTransform();
            }

            Report(_contactSolver.VelocityConstraints);
        }

        public void Add(Body body)
        {
            Debug.Assert(_bodyCount < _bodyCapacity);
            body.IslandIndex = _bodyCount;
            _bodies[_bodyCount++] = body;
        }

        public void Add(Contact contact)
        {
            Debug.Assert(_contactCount < _contactCapacity);
            _contacts[_contactCount++] = contact;
        }

        public void Add(Joint joint)
        {
            Debug.Assert(_jointCount < _jointCapacity);
            _joints[_jointCount++] = joint;
        }

        private void Report(ContactVelocityConstraint[] constraints)
        {
            if (_contactManager == null)
                return;

            for (int i = 0; i < _contactCount; ++i)
            {
                Contact c = _contacts[i];

                //Velcro feature: added after collision
                c._fixtureA.AfterCollision?.Invoke(c._fixtureA, c._fixtureB, c, constraints[i]);
                c._fixtureB.AfterCollision?.Invoke(c._fixtureB, c._fixtureA, c, constraints[i]);

                //Velcro optimization: We don't store the impulses and send it to the delegate. We just send the whole contact.
                _contactManager.PostSolve?.Invoke(c, constraints[i]);
            }
        }
    }
}