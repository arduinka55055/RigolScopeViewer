import sys
import time
import numpy as np
import matplotlib.pyplot as plt
import pyvisa

class RigolDHO914:
    """
    A class to interface with the Rigol DHO914 oscilloscope using PyVISA.
    """
    def __init__(self, resource_string=None, ip_address=None):
        """
        Initializes the connection to the oscilloscope.
        
        Args:
            resource_string (str, optional): The VISA resource string.
            ip_address (str, optional): The IP address of the scope on the network.
        """
        try:
            # Explicitly force PyVISA to use the pure Python backend
            self.rm = pyvisa.ResourceManager('@py')
        except AttributeError as e:
            import sys
            print(f"\n[!] CRITICAL ERROR: {e}")
            print("[!] This usually happens if you named your script 'pyvisa.py' or have a folder named 'pyvisa' in this directory.")
            print(f"[!] Currently loading pyvisa from: {pyvisa.__file__}")
            print("[!] FIX: Rename your file from 'pyvisa.py' to 'rigol_viewer.py' and delete any compiled 'pyvisa.pyc' files.\n")
            sys.exit(1)
        except Exception as e:
            import sys
            print(f"\n[!] CRITICAL ERROR: Could not load VISA backend.")
            print(f"Error details: {e}")
            sys.exit(1)
            
        self.scope = None
        
        if ip_address:
            self.resource_string = f"TCPIP::{ip_address}::INSTR"
        else:
            self.resource_string = resource_string
        
    def connect(self):
        """Establishes connection to the oscilloscope."""
        try:
            if not self.resource_string:
                # Attempt to find the instrument if no string is provided
                resources = self.rm.list_resources()
                print(f"Found VISA resources: {resources}")
                # Simple heuristic: look for USB or TCPIP strings that might be the scope
                # In a real application, you'd want a more robust way to select the right one
                # or require the user to provide it.
                for res in resources:
                    if 'USB' in res or 'TCPIP' in res:
                        self.resource_string = res
                        print(f"Auto-selected resource: {self.resource_string}")
                        break
                
                if not self.resource_string:
                     raise ValueError("No suitable VISA resource found automatically.")

            self.scope = self.rm.open_resource(self.resource_string)
            # Important: Set a reasonable timeout for large data transfers
            self.scope.timeout = 10000 
            
            # Query identification string to verify connection
            idn = self.scope.query("*IDN?")
            print(f"Connected to: {idn.strip()}")
            return True
            
        except pyvisa.VisaIOError as e:
            print(f"VISA Error during connection: {e}")
            return False
        except Exception as e:
            print(f"Error connecting: {e}")
            return False

    def get_waveform(self, channel=1):
        """
        Retrieves the waveform data from the specified channel.
        
        Args:
            channel (int): The channel number to read (1-4).
            
        Returns:
            tuple: (time_array, voltage_array) as numpy arrays, or (None, None) on failure.
        """
        if not self.scope:
            print("Not connected to oscilloscope.")
            return None, None
            
        try:
            # 1. Set the data source
            self.scope.write(f":WAVeform:SOURce CHANnel{channel}")
            
            # 2. Set the format to BYTE for faster transfer (ASCII is slow)
            self.scope.write(":WAVeform:FORMat BYTE")
            
            # 3. Set the mode. 'NORM' reads the data currently displayed on screen.
            # 'RAW' reads deep memory (might be huge and slow). We use NORM for this demo.
            self.scope.write(":WAVeform:MODE NORMal")
            
            # 4. Get waveform preamble (parameters needed to interpret the raw data)
            preamble = self.scope.query(":WAVeform:PREamble?")
            # Preamble format: format, type, points, count, xincrement, xorigin, xreference, yincrement, yorigin, yreference
            # Note: The specific format might vary slightly between Rigol series, 
            # but DHO900 usually follows this standard SCPI format.
            vals = preamble.split(',')
            
            if len(vals) < 10:
                print(f"Error parsing preamble: {preamble}")
                return None, None
                
            points      = int(vals[2])
            xincrement  = float(vals[4])
            xorigin     = float(vals[5])
            xreference  = float(vals[6])
            yincrement  = float(vals[7])
            yorigin     = float(vals[8])
            yreference  = float(vals[9])

            print(f"Expecting {points} points...")

            # 5. Read the raw waveform data
            # The data starts with a TMC block header (e.g., #800001000...)
            # query_binary_values handles stripping this header and converting to a list/array
            raw_data = self.scope.query_binary_values(":WAVeform:DATA?", datatype='B', container=np.array)
            
            # 6. Convert raw byte data to voltage and create time vector
            # Voltage = (RawData - Yreference) * Yincrement + Yorigin
            voltage = (raw_data - yreference) * yincrement + yorigin
            
            # Time = (DataPointIndex - Xreference) * Xincrement + Xorigin
            time_vector = np.arange(len(raw_data)) * xincrement + xorigin

            return time_vector, voltage

        except pyvisa.VisaIOError as e:
            print(f"VISA Error reading waveform: {e}")
            return None, None
        except Exception as e:
             print(f"Error reading waveform: {e}")
             return None, None
             
    def close(self):
        """Closes the connection to the oscilloscope."""
        if self.scope:
            self.scope.close()
            print("Connection closed.")

def main():
    """Main function to run the demonstration."""
    print("Starting Rigol DHO914 Waveform Viewer Demo...")
    
    # Initialize the scope class using your IP address!
    scope = RigolDHO914(ip_address="192.168.0.162")
    
    if not scope.connect():
        print("Exiting due to connection failure.")
        return

    print("Fetching waveform from Channel 1...")
    time_data, volt_data = scope.get_waveform(channel=2)
    
    scope.close()
    
    if time_data is not None and volt_data is not None:
        print("Plotting waveform...")
        plt.figure(figsize=(10, 6))
        
        # Plot the data
        plt.plot(time_data, volt_data, color='yellow', label='CH1')
        
        # Make it look a bit like an oscilloscope screen
        plt.title('Rigol DHO914 Waveform - CH1')
        plt.xlabel('Time (s)')
        plt.ylabel('Voltage (V)')
        plt.grid(True, linestyle='--', alpha=0.7)
        plt.gca().set_facecolor('black') # Dark background
        plt.legend()
        plt.tight_layout()
        
        # Show the plot
        plt.show()
    else:
        print("Failed to acquire data for plotting.")

if __name__ == "__main__":
    main()