import React from 'react';

interface Bus3DProps {
  color?: string;
  label?: string;
  selected?: boolean;
}

const Bus3D: React.FC<Bus3DProps> = ({ color = '#00f0ff', label, selected }) => {
  // A CSS 3D box representing a bus
  const width = 12; // width of bus in px
  const length = 32; // length of bus in px
  const height = 14; // height of bus in px

  const faceStyle: React.CSSProperties = {
    position: 'absolute',
    border: `1px solid rgba(0,0,0,0.5)`,
    boxSizing: 'border-box',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontWeight: 'bold',
    fontSize: '8px',
    color: '#000',
    backfaceVisibility: 'hidden',
  };

  return (
    <div 
      className="bus-3d-wrapper"
      style={{
        width: `${width}px`,
        height: `${length}px`,
        position: 'relative',
        transformStyle: 'preserve-3d',
        transform: `translateZ(${height / 2}px) ${selected ? 'scale(1.3)' : 'scale(1)'}`,
        transition: 'transform 0.3s ease',
        cursor: 'pointer'
      }}
    >
      {/* Top Face */}
      <div style={{
        ...faceStyle,
        width: `${width}px`,
        height: `${length}px`,
        background: color,
        transform: `translateZ(${height / 2}px)`,
        color: '#000',
        boxShadow: selected ? `0 0 15px ${color}` : 'none'
      }}>
        {label}
      </div>

      {/* Bottom Face */}
      <div style={{
        ...faceStyle,
        width: `${width}px`,
        height: `${length}px`,
        background: '#111',
        transform: `translateZ(-${height / 2}px) rotateY(180deg)`
      }} />

      {/* Front Face (pointing up in 2D, which is Y negative in CSS, wait: length is along Y axis. 0 deg bearing is North, which is -Y in CSS? No, if rotationAlignment=map, North is UP on the screen, which is -Y. So Front is -Y) */}
      <div style={{
        ...faceStyle,
        width: `${width}px`,
        height: `${height}px`,
        background: '#fff', // windshield
        transform: `translateY(-${height / 2}px) rotateX(90deg)`,
        borderBottom: `4px solid ${color}` // bumper
      }} />

      {/* Back Face */}
      <div style={{
        ...faceStyle,
        width: `${width}px`,
        height: `${height}px`,
        background: `${color}ee`,
        transform: `translateY(${length - height / 2}px) rotateX(-90deg)`
      }} />

      {/* Left Face */}
      <div style={{
        ...faceStyle,
        width: `${length}px`,
        height: `${height}px`,
        background: `${color}dd`,
        transform: `translateX(-${length / 2 - width / 2}px) translateY(${length / 2 - height / 2}px) rotateY(-90deg) rotateZ(90deg)`
      }}>
        {/* Windows */}
        <div style={{ width: '80%', height: '40%', background: '#333', marginTop: '-4px' }} />
      </div>

      {/* Right Face */}
      <div style={{
        ...faceStyle,
        width: `${length}px`,
        height: `${height}px`,
        background: `${color}dd`,
        transform: `translateX(${width / 2}px) translateY(${length / 2 - height / 2}px) rotateY(90deg) rotateZ(-90deg)`
      }}>
        <div style={{ width: '80%', height: '40%', background: '#333', marginTop: '-4px' }} />
      </div>
    </div>
  );
};

export default Bus3D;
