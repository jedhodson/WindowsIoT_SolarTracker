/*
  Code for Windows 10 Solar Tracker with live data feed.
  Arduino Side code - Tracking system / motor control
  Communication over I2C to Pi

  Released under GNU GENERAL PUBLIC LICENSE VERSION 3+

  Full project details avaliable on Hackster.io
  Created by Jed Hodson 2015
 */

#include <Wire.h>
#include <Servo.h>

#define SLAVE_ADDRESS 0x0A

int receivedData = 0;
int toPushPos = 0;

//Servo and tracking

Servo horizontal;
int servoh = 180;

int servohLimitHigh = 180;
int servohLimitLow = 65;


Servo vertical;
int servov = 45;

int servovLimitHigh = 80;
int servovLimitLow = 15;

int horiServoHome = 109; //Horizontal Servo Home Position
int vertServoHome = 0; //Vertical Servo Home Postion

// LDR pin connections
//  name  = analogpin;
int ldrlt = 0; //LDR top left
int ldrrt = 1; //LDR top right
int ldrld = 2; //LDR down left
int ldrrd = 3; //ldr down right

bool paused = false;
const int delayTime = 10;

bool debug = false;

void setup()
{
  Serial.begin(9600);

  horizontal.attach(11);
  vertical.attach(10);
  
  horizontal.write(horiServoHome);
  vertical.write(vertServoHome);
  
  Wire.begin(SLAVE_ADDRESS);
  Wire.onReceive(onReceiveDataEvent);
}

void loop()
{
  delay(100);
}

void onReceiveDataEvent(int byteCount)
{
  while (Wire.available())
  {
    receivedData = Wire.read();
    debugSend();

    if (receivedData == 0x0F)
    {
      runTracker();
    }
    else if (receivedData == 0x00)
    {
      killAll();
    }
    else if (receivedData == 0x01)
    {
      systemResume();
    }
    else if (receivedData == 0x02)
    {
      homeServos();
    }
    else if (receivedData == 0x0A)
    {
      //Servo Pos Forced
      receivedData = Wire.read();

      if (receivedData == 0x0B)
      {
        //Force H Servo
        toPushPos = Wire.read();
        forceServo(0, toPushPos);

      }
      else if (receivedData = 0x0C)
      {
        //Force V Servo
        toPushPos = Wire.read();
        forceServo(1, toPushPos);
      }
    }
  }
}

void debugSend()
{
  if (debug == true)
  {
    Serial.print("Data Received: ");
    Serial.println(receivedData);
  }
}

void killAll()
{
  // 0x00
  paused = true;
  if (horizontal.attached() == true)
  {
    horizontal.detach();
  }
  if (vertical.attached() == true)
  {
    vertical.detach();
  }
  else
  {
    //Already detached. Do nothing
  }
}

void systemResume()
{
  // 0x01
  paused = false;
  horizontal.attach(11);
  vertical.attach(10);
}

//Reset Servo Positions
void homeServos()
{
  //0x02
  paused = true;
  horizontal.write(horiServoHome);
  vertical.write(vertServoHome);
}

void runTracker()
{
  //0x0F
  if (paused != true)
  {
    int lt = analogRead(ldrlt); // top left
    int rt = analogRead(ldrrt); // top right
    int ld = analogRead(ldrld); // down left
    int rd = analogRead(ldrrd); // down rigt

    // int dtime = analogRead(4)/20; // read potentiometers
    // int tol = analogRead(5)/4;
    int dtime = 10;
    int tol = 50;

    int avt = (lt + rt) / 2; // average value top
    int avd = (ld + rd) / 2; // average value down
    int avl = (lt + ld) / 2; // average value left
    int avr = (rt + rd) / 2; // average value right

    int dvert = avt - avd; // check the diffirence of up and down
    int dhoriz = avl - avr;// check the diffirence og left and right

    if (debug == true)
    {
      Serial.print(avt);
      Serial.print(" ");
      Serial.print(avd);
      Serial.print(" ");
      Serial.print(avl);
      Serial.print(" ");
      Serial.print(avr);
      Serial.print("   ");
      Serial.print(dtime);
      Serial.print("   ");
      Serial.print(tol);
      Serial.println(" ");
    }

    if (-1 * tol > dvert || dvert > tol) // check if the diffirence is in the tolerance else change vertical angle
    {
      if (avt > avd)
      {
        servov = ++servov;
        if (servov > servovLimitHigh)
        {
          servov = servovLimitHigh;
        }
      }
      else if (avt < avd)
      {
        servov = --servov;
        if (servov < servovLimitLow)
        {
          servov = servovLimitLow;
        }
      }
      vertical.write(servov);
    }

    if (-1 * tol > dhoriz || dhoriz > tol) // check if the diffirence is in the tolerance else change horizontal angle
    {
      if (avl > avr)
      {
        servoh = --servoh;
        if (servoh < servohLimitLow)
        {
          servoh = servohLimitLow;
        }
      }
      else if (avl < avr)
      {
        servoh = ++servoh;
        if (servoh > servohLimitHigh)
        {
          servoh = servohLimitHigh;
        }
      }
      else if (avl = avr)
      {
        // nothing
      }
      horizontal.write(servoh);
    }
    delay(delayTime);
  }
}

void forceServo(int servoName, int servoPos)
{
  paused = true;
  //Force Servo Pos
  //Servo Name 0 - H Servo
  //Servo Name 1 = V Servo
  if (servoName == 0)
  {
    //For H Servo
    horizontal.write(servoPos);
  }
  else if (servoName == 1)
  {
    //For V Servo
    vertical.write(servoPos);
  }
}

/*
  Other Info -----------------
    Kill System         - 0x00
    Resume System       - 0x01
    Home Servos         - 0x02

    Force Servo (Any)   - 0x0A
    H Servo             - 0x0B
    V Servo             - 0x0C
    Pos to Send         - DEC (variable)

    Run Tracking System - 0x0F
*/


